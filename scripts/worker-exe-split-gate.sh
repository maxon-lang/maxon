#!/usr/bin/env bash
#
# THE WORKER-EXE SPLIT GATE — does `spec-test` really test the compiler it was TOLD to test?
#
# The spec suite is 2,372 Maxon programs. Not one of them can observe which BINARY the harness
# spawned to build it, so the harness's own dispatch is the one part of this project the spec suite
# is structurally blind to. This script is that blind spot's test.
#
# ============================================================================================
# WHAT IT IS GUARDING, AND WHY A GREEN SUITE COULD NEVER SEE IT
# ============================================================================================
#
# `Main.runSpecTest` resolves TWO executables that are ONE FILE IN-TREE:
#
#   workerExe    what a pool worker IS — this harness, relaunched with `--worker-persistent`
#   compilerExe  the COMPILER UNDER TEST — spawned as `<exe> build <fragment>` for each test
#
# Until 2026-07-29 that was one parameter, and the worker was never even told the second one: it
# re-derived it from its own `Process.executablePath()`. In-tree that answers correctly, every
# time, by coincidence — the harness IS the compiler. So the conflation was never wrong here, was
# never tested, and was invisible to every green suite.
#
# The 🚩 Phase 1 gate is precisely the configuration that separates them: a harness compiled by
# `maxon.exe` and a harness compiled by `maxon-shv2.exe`, BOTH driving `maxon-shv2.exe` as the
# compiler under test, and required to agree. Unsplit, that gate does not fail loudly — IT PASSES.
# Both harnesses would delegate every verdict to the same in-tree binary, the two result streams
# would agree perfectly, and the gate would certify that shv2 compiles the harness correctly
# without a single test having been run by shv2-compiled code.
#
# ============================================================================================
# THE EXPERIMENT — AND WHY THE OBVIOUS ONE PROVES NOTHING
# ============================================================================================
#
# The obvious check is "run with `--compiler=<a copy of the compiler>` and see it pass". That is
# necessary but it is NOT evidence: the copy is byte-identical, so a harness that honoured the flag
# and a harness that ignored it and used itself BOTH pass. No case distinguishes the two models,
# and "no failing test" is a statement about coverage, never about agreement.
#
# The distinguishing experiment is a NEGATIVE CONTROL: point `--compiler=` at a real, executable
# file that is NOT a compiler.
#
#   split honoured    the worker spawns it, it does not compile anything, every test FAILS
#   split broken      the worker ignores it and builds with itself, every test PASSES
#
# Those are opposite verdicts, which is the entire point. Verified by sabotage on 2026-07-29:
# reverting `runSpecTest` to pass one value for both jobs turned this gate's negative control from
# `0 passed, 4 failed` into `4 passed, 0 failed`.
#
#   ⚠ A NEGATIVE CONTROL THAT CANNOT GO RED IS DECORATION. If you change this script, re-run the
#     sabotage — pass `workerExe` where `compilerExe` belongs in `Main.runSpecTest` — and confirm
#     CHECK 3 below goes red before you trust a green run of it.
#
# Usage:  scripts/worker-exe-split-gate.sh [--compiler-exe=<path>]
# Exit:   0 = all checks pass · 1 = a check failed (a rung-halting red gate)

set -u

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT" || exit 1

# The harness under examination. It plays the PARENT in every check below; what changes between
# checks is only what it is told to use as the compiler.
HARNESS="./maxon-shv2/.maxon/maxon-shv2"
[ -f "${HARNESS}.exe" ] && HARNESS="${HARNESS}.exe"

for arg in "$@"; do
	case "$arg" in
		--compiler-exe=*) HARNESS="${arg#*=}" ;;
		*) echo "usage: $0 [--compiler-exe=<path>]" >&2; exit 1 ;;
	esac
done

if [ ! -f "$HARNESS" ]; then
	echo "error: no shv2 binary at $HARNESS — run 'maxon build maxon-shv2' first" >&2
	exit 1
fi

# One spec, four tests, no filesystem or subprocess surface of its own: the checks below are about
# WHICH COMPILER RAN, so the tests themselves must be the least interesting programs in the suite.
FILTER="arithmetic/"
EXPECTED_SELECTED=4

WORK="temp/worker-exe-split-gate"
rm -rf "$WORK"
mkdir -p "$WORK/cut" || exit 1

FAILURES=0

# `pass`/`fail` print the verdict AND the evidence line, because a gate that says only "FAILED"
# sends you back to run it again to find out what it saw.
pass() { printf '  PASS  %s\n' "$1"; }
fail() { printf '  FAIL  %s\n         saw: %s\n' "$1" "$2"; FAILURES=$((FAILURES + 1)); }

# ⚠ ONE SUITE AT A TIME IN ONE TREE. The runner shares `.spec-tmp`, so two overlapping runs produce
# a FALSE RED in a lane that is actually green. Every check below is sequential and blocking.
run_suite() { "$HARNESS" spec-test "--filter=$FILTER" "$@" > "$WORK/out.log" 2>&1; echo "$?"; }
summary()   { grep -E '^[0-9]+ passed, [0-9]+ failed' "$WORK/out.log" | tail -1; }

echo "THE WORKER-EXE SPLIT GATE"
echo "  harness: $HARNESS"
echo "  filter:  $FILTER ($EXPECTED_SELECTED tests)"
echo

# --------------------------------------------------------------------------------------------
# CHECK 1 — the in-tree default is unchanged.
#
# No `--compiler` at all: the compiler under test IS the running executable, which is what every
# other invocation in this project relies on. This check exists so the split cannot be bought by
# breaking the ordinary case.
# --------------------------------------------------------------------------------------------
code="$(run_suite)"
got="$(summary)"
if [ "$code" = "0" ] && [ "$got" = "$EXPECTED_SELECTED passed, 0 failed" ]; then
	pass "in-tree default (no --compiler) still self-tests"
else
	fail "in-tree default (no --compiler) still self-tests" "exit=$code; $got"
fi

# --------------------------------------------------------------------------------------------
# CHECK 2 — POSITIVE CONTROL: a real compiler at a path that is NOT the worker's path.
#
# The parent stays $HARNESS; the compiler under test is a copy somewhere else. This proves the flag
# is plumbed all the way to the fragment build and that the harness works at all when the two paths
# differ. On its own it distinguishes nothing (see the header) — CHECK 3 is what has teeth.
#
# The copy locates `stdlib/` by walking up from its OWN path (StdlibSource.locateStdlibDir), so it
# must live inside the checkout. `temp/` is two levels down from the repo root; that is why.
# --------------------------------------------------------------------------------------------
CUT="$WORK/cut/$(basename "$HARNESS")"
cp "$HARNESS" "$CUT" || exit 1

code="$(run_suite "--compiler=$CUT")"
got="$(summary)"
if [ "$code" = "0" ] && [ "$got" = "$EXPECTED_SELECTED passed, 0 failed" ]; then
	pass "positive control: real compiler at a different path"
else
	fail "positive control: real compiler at a different path" "exit=$code; $got"
fi

# --------------------------------------------------------------------------------------------
# CHECK 3 — NEGATIVE CONTROL. THIS IS THE GATE. Everything above is scaffolding.
#
# The compiler under test is a real executable that is not a compiler: it ignores its arguments,
# writes nothing, and exits 1. If the worker honours the forwarded `--compiler`, no fragment can be
# built and EVERY selected test fails. If the worker re-derives its compiler from its own path,
# every test passes and the flag was decoration.
#
# The stub is built by the harness itself rather than committed: a checked-in binary would be a
# platform-specific artifact, and this way the stub is always for the host being gated.
# --------------------------------------------------------------------------------------------
cat > "$WORK/notacompiler.maxon" <<'MAXON'
function main() returns ExitCode
	return 1
end 'main'
MAXON

if ! "$HARNESS" build "$WORK/notacompiler.maxon" -o "$WORK/notacompiler" > "$WORK/stub-build.log" 2>&1; then
	echo "  ERROR could not build the negative-control stub — see $WORK/stub-build.log" >&2
	exit 1
fi

STUB="$WORK/notacompiler"
[ -f "${STUB}.exe" ] && STUB="${STUB}.exe"

code="$(run_suite "--compiler=$STUB")"
got="$(summary)"
if [ "$code" != "0" ] && [ "$got" = "0 passed, $EXPECTED_SELECTED failed" ]; then
	pass "NEGATIVE CONTROL: a non-compiler under test fails every test"
else
	fail "NEGATIVE CONTROL: a non-compiler under test fails every test" \
	     "exit=$code; $got  <-- the worker did NOT use the compiler it was given"
fi

# --------------------------------------------------------------------------------------------
# CHECK 4 — a bad `--compiler` is refused ONCE, by the parent, before any worker exists.
#
# Same discipline as `requireRunnerAvailable`: a missing binary discovered per test turns one fact
# into hundreds of identical failures, and discovered inside a worker it turns into a dead pipe
# whose real message is in another process's stderr.
# --------------------------------------------------------------------------------------------
"$HARNESS" spec-test "--filter=$FILTER" --compiler="$WORK/does-not-exist" > "$WORK/out.log" 2>&1
code=$?
if [ "$code" != "0" ] && grep -q 'must name an existing executable' "$WORK/out.log"; then
	pass "a nonexistent --compiler is refused up front"
else
	fail "a nonexistent --compiler is refused up front" "exit=$code; $(head -1 "$WORK/out.log")"
fi

echo
if [ "$FAILURES" -eq 0 ]; then
	echo "GATE PASSED — the compiler under test and the worker executable are separate facts."
	exit 0
fi

echo "GATE FAILED — $FAILURES check(s) red. Logs under $WORK/."
exit 1
