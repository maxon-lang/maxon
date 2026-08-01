#!/usr/bin/env bash
#
# THE OUTPUT-LOCK GATE — when a build CANNOT replace its own output, does it say so usefully?
#
# `E6002` is the one diagnostic in this compiler that no spec case can ever reach, and the reason is
# structural rather than incidental:
#
#   * It is the refusal arm of `Compiler.discardPreviousOutput`, and the suite runs the SUCCESS arm
#     constantly: `specs-shv2/.spec-tmp/<spec>/` is not cleaned between runs, so every re-run of a
#     spec finds the previous run's exe at the same `-o` path and deletes it. The branch is evaluated
#     thousands of times per suite and taken exactly never — which is the worst shape a branch can
#     have, because the coverage looks incidental rather than absent.
#   * Taking it needs the filesystem to REFUSE that unlink — a running image on Windows, a read-only
#     attribute, a non-writable parent directory on POSIX. A spec is a Maxon program plus an expected
#     exit code or stderr; the parser's whole directive set is `test:` / `disabled-test:`, a ```maxon
#     block, ```exitcode, ```maxoncstderr, `status:` and `// --- file:`. Not one of them can change a
#     permission, the harness (not the spec) owns the output path, and the program a spec describes
#     does not run until long after the compile that would have to fail.
#
# So the whole branch — a refusal that exists to stop the compiler reporting success over a stale
# binary — had ZERO executable coverage. This script is that coverage. It is a shell gate for the same
# reason `worker-exe-split-gate.sh` and `spec-wedge-repro.sh` are: the property lives OUTSIDE the
# program under test, in the state of the filesystem around it.
#
# ============================================================================================
# WHAT IT ASSERTS, AND WHY EACH PART IS LOAD-BEARING
# ============================================================================================
#
# 1. The build FAILS (non-zero exit) when the output cannot be replaced.
# 2. It fails with E6002 specifically.
# 3. The message names the LIKELY cause — a spec-test or build in this tree still running — and not
#    only the mechanical one. "Locked or read-only" alone reads exactly like a broken compiler, which
#    is how this cost two sessions their time before the message was fixed.
# 4. The message quotes a WAIT-OR-KILL deadline in seconds. It is interpolated from the spec pool's
#    `WedgeWatchdogMs`, so the gate checks its SHAPE and never its value: asserting the number here
#    would make this script a second copy of a constant whose entire point is having one copy.
# 5. The message states that the previous binary SURVIVED — and CHECK 6 proves that claim true by
#    hash. This is the one branch where a failed build leaves an output behind, which is exactly why a
#    `;`-chained `build; suite` can report a false red or a FALSE GREEN off the stale binary, and why
#    the exit code is the only honest signal that a build happened.
# 6. NEGATIVE CONTROL: with the lock removed and nothing else changed, the same build succeeds and
#    emits no E6002. Without it, a compiler that refused every build would score a perfect green here.
#
#   ⚠ CHECKS 3-5 MATCH ON WORDING, deliberately: the wording IS the property here, so a reword of
#     `discardPreviousOutput`'s message is a change to what this gate certifies and must be made in
#     both places. There is no way to assert "the message is useful" that does not read the message.
#
#   ⚠ VERIFIED TO GO RED: run against the binary built from the parent of the commit that added this
#     script, CHECKS 3-5 fail (the old message stopped at "it is locked or read-only, so this build
#     cannot replace it") while 1, 2, 6 and 7 pass. That split is the point — the failure mode this
#     gate guards is a message that is TRUE and useless, not a build that succeeds when it should not.
#
# Usage:  scripts/output-lock-gate.sh
# Exit:   0 = all checks pass · 1 = a check failed · 2 = the gate could not run (setup failure)

set -u

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT" || exit 2

case "$(uname -s)" in
	MINGW*|MSYS*|CYGWIN*) EXE_EXT=".exe"; HOST_IS_WINDOWS=1 ;;
	*)                    EXE_EXT="";     HOST_IS_WINDOWS=0 ;;
esac

SHV2="./maxon-shv2/.maxon/maxon-shv2${EXE_EXT}"
WORK="temp/output-lock-gate"
SRC="$WORK/prog.maxon"
OUT="$WORK/prog${EXE_EXT}"

FAILED=0

pass() { printf '  PASS  %s\n' "$1"; }
fail() { printf '  FAIL  %s\n' "$1"; FAILED=1; }

# Make `$OUT` impossible to unlink, and put it back. The two hosts need different levers and the
# difference is not cosmetic: a POSIX unlink is authorised by the DIRECTORY's write bit, so clearing
# the file's own mode there would change nothing at all and the gate would silently test nothing.
lock_output() {
	if [ "$HOST_IS_WINDOWS" = "1" ]; then
		attrib +R "$(cygpath -w "$OUT")" || return 1
	else
		chmod a-w "$WORK" || return 1
	fi
	return 0
}

unlock_output() {
	if [ "$HOST_IS_WINDOWS" = "1" ]; then
		attrib -R "$(cygpath -w "$OUT")" 2>/dev/null
	else
		chmod u+w "$WORK" 2>/dev/null
	fi
	return 0
}

# The lock must not outlive this script whatever happens to it — a read-only directory or image left
# in the tree would break every later build in exactly the way this gate exists to describe.
cleanup() { unlock_output; }
trap cleanup EXIT INT TERM

if [ ! -x "$SHV2" ]; then
	printf 'output-lock-gate: %s is missing — build it first (`./bin/maxon%s build maxon-shv2`)\n' "$SHV2" "$EXE_EXT" >&2
	exit 2
fi

if [ "$HOST_IS_WINDOWS" = "0" ] && [ "$(id -u)" = "0" ]; then
	printf 'output-lock-gate: refusing to run as root — root ignores the directory write bit, so the lock would not lock\n' >&2
	exit 2
fi

rm -rf "$WORK" || exit 2
mkdir -p "$WORK" || exit 2

cat > "$SRC" <<'MAXON'
export function main() returns ExitCode
	return 7
end 'main'
MAXON

printf '=== output-lock gate ===\n'

# ---- CHECK 1: the baseline build succeeds and produces the output -------------------------------
"$SHV2" build "$SRC" -o "$WORK/prog" > "$WORK/build-baseline.log" 2>&1
BASELINE_STATUS=$?

if [ "$BASELINE_STATUS" -eq 0 ] && [ -f "$OUT" ]; then
	pass "CHECK 1: baseline build succeeded and wrote $OUT"
else
	fail "CHECK 1: baseline build exited $BASELINE_STATUS and left $OUT $( [ -f "$OUT" ] && echo present || echo absent ) — see $WORK/build-baseline.log"
	printf '\noutput-lock-gate: the setup build failed; the rest of the gate would measure nothing.\n' >&2
	exit 2
fi

BEFORE_HASH="$(git hash-object "$OUT")"

if ! lock_output; then
	printf 'output-lock-gate: could not make %s un-removable\n' "$OUT" >&2
	exit 2
fi

# ---- CHECK 2: the locked rebuild fails, with E6002 ----------------------------------------------
"$SHV2" build "$SRC" -o "$WORK/prog" > "$WORK/build-locked.log" 2>&1
LOCKED_STATUS=$?

if [ "$LOCKED_STATUS" -ne 0 ]; then
	pass "CHECK 2a: the locked rebuild failed (exit $LOCKED_STATUS)"
else
	fail "CHECK 2a: the locked rebuild exited 0 — a build that cannot replace its output must not report success"
fi

if grep -q 'error E6002' "$WORK/build-locked.log"; then
	pass "CHECK 2b: it failed with E6002"
else
	fail "CHECK 2b: no 'error E6002' in $WORK/build-locked.log"
fi

# ---- CHECKS 3-5: the message earns its place -----------------------------------------------------
if grep -q 'still running' "$WORK/build-locked.log" && grep -q 'has not exited' "$WORK/build-locked.log"; then
	pass "CHECK 3: the message names the likely cause (a run in this tree that has not exited)"
else
	fail "CHECK 3: the message does not name the likely cause — it says only what the OS said, which reads as a broken build"
fi

if grep -Eq 'aborts itself after [0-9]+\.[0-9] s' "$WORK/build-locked.log"; then
	pass "CHECK 4: the message quotes a wait-or-kill deadline in seconds"
else
	fail "CHECK 4: the message quotes no deadline, so the reader cannot tell 'wait' from 'this is broken'"
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

"$SHV2" build "$SRC" -o "$WORK/prog" > "$WORK/build-unlocked.log" 2>&1
UNLOCKED_STATUS=$?

if [ "$UNLOCKED_STATUS" -eq 0 ] && ! grep -q 'error E6002' "$WORK/build-unlocked.log"; then
	pass "CHECK 7: with the lock removed the same build succeeds and emits no E6002"
else
	fail "CHECK 7: the unlocked rebuild exited $UNLOCKED_STATUS — E6002 is firing on builds it should not, so CHECKS 2-6 prove nothing"
fi

printf '\n'

if [ "$FAILED" -eq 0 ]; then
	printf 'output-lock gate: PASS\n'
	exit 0
fi

printf 'output-lock gate: FAIL — logs in %s\n' "$WORK"
exit 1
