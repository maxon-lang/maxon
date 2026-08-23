#!/usr/bin/env bash
#
# ⛔ DELIBERATELY NOT `set -e`, AND THAT IS THE POINT OF THIS FILE.
#
# This script holds TWO kinds of step, and one `set -e` chain treated them as one kind:
#
#   * A GATE is an INDEPENDENT CHECK. Its verdict says nothing about whether the next check can run,
#     so a red one must not stop the others — it must be reported and the run must carry on.
#   * A BUILD produces the binary its successors are RUN AGAINST. A gate downstream of a failed build
#     has nothing to test, and running it anyway would manufacture a false red. Those are SKIPPED.
#
# The cost of conflating them was measured, not hypothetical. `check-debug-goldens.sh` went red on
# 2026-08-01 (four green-thread transcripts, filed as A3b in another lane). It is the fourth step, so
# `set -e` killed the run there — and the shv2 build, the shv2 suite, the output-lock gate, the
# stale-binary gate, the MCP build and the MCP tests DID NOT RUN FOR FIVE DAYS, in the script whose
# entire job is to run them. SIX GATES THAT LOOKED LIKE COVERAGE. Repairing A3b would not have fixed
# that: the next red gate would hide everything after it again.
#
# ⚠ NOTHING HERE IS ALLOWED TO BE "EXPECTED RED". A known-red gate the runner has been taught to
# accept is the same lie in a politer form. A3b's four failures report as FAILURES, and this script
# exits non-zero because of them, until the lane that owns them fixes them.
#
# THE ONE PLACE THE DISTINCTION IS WRITTEN DOWN is the `requires` column of the step table below. A
# step blocks another EXACTLY WHEN that other names it — the blocking/independent split is derived
# from that one column and stated nowhere else, so it cannot come to disagree with itself.
set -u

# One row per step: <key>|<requires>|<heading>|<command>
#
# `requires` is `-` for none, or a comma-separated list of keys that must have PASSED. Skipping is
# transitive without any extra machinery: a step whose prerequisite was itself skipped is not PASS
# either. The command is the LAST field, so it may contain `|` freely.
STEPS=(
	"bootstrap|-|Building C# Compiler|dotnet build maxon-sharp"

	"csharp-suite|bootstrap|Running C# Spec Tests|bin/maxon spec-test"

	# The SECOND suite run is the whole point: `Compiler.DebugInfo` is [ThreadStatic] and the harness
	# leaves it OFF, so the default run compiles all ~3200 programs down the path `maxon build` does NOT
	# take. That hole once shipped a crash on a program this very suite compiles every run. A per-test
	# `<!-- DebugInfo -->` directive covers one compile at a time; only this covers the corpus.
	#
	# It is also the only place the "pure observer" contract is MEASURED rather than asserted: the run
	# verifies every committed fragment golden, and those goldens were minted with debug info off — so a
	# green run here is proof that producing a sidecar changed not one emitted byte.
	#
	# It was an environment variable (MAXON_SPEC_DEBUG_INFO=1) that nothing in this tree ever set, which
	# is the same as not having it. A switch nobody turns on cannot fail.
	"csharp-debuginfo|bootstrap|Running C# Spec Tests with debug info ON|bin/maxon spec-test --debug-info"

	# Gates the debugger/profiler/coverage sample transcripts, which the spec suite does not cover:
	# their acceptance is a golden transcript, not a spec test. Runs before the (long) shv2 build
	# because it needs only the bootstrap — it no longer decides whether that build happens at all.
	"debug-goldens|bootstrap|Checking Debugger Goldens|bash scripts/check-debug-goldens.sh"

	"shv2|bootstrap|Building shv2 Compiler|bin/maxon build maxon-shv2"

	"shv2-suite|shv2|Running shv2 Spec Tests|maxon-shv2/.maxon/maxon-shv2 spec-test"

	# Gates what E6002 SAYS, which the spec suite structurally cannot reach: taking that branch needs the
	# filesystem to refuse an unlink, and a spec's directive set cannot change a permission. The suite runs
	# the success arm thousands of times per run and the refusal arm never — coverage that looks incidental
	# rather than absent, which is the worst shape a branch can have.
	#
	# Requires the shv2 build because it drives that binary. It deliberately makes a build FAIL and
	# asserts the wording of the refusal, so a failure here is the gate working, not the tree being broken.
	"output-lock|shv2|Checking the output-lock refusal (E6002)|bash scripts/output-lock-gate.sh"

	# Gates the OTHER half of the same hazard: `output-lock-gate.sh` proves a build that cannot replace
	# its output says so, and this proves the harness refuses to report a verdict off the binary such a
	# build leaves behind. Neither is spec-testable — no Maxon program can make the binary compiling it
	# older than the tree around it — and the property is worth exactly as much as the check nobody runs.
	#
	# Requires the shv2 build because it drives that binary; it ages a source's MTIME forward and puts it
	# back, so no file's bytes are touched and the working tree is unchanged either way.
	"stale-binary|shv2|Checking the stale-binary refusal|bash scripts/stale-binary-gate.sh"

	# Gates the PROCESS-WIDE stdlib token memo (G20): that a warm compile is served from it, that it
	# stays bounded (`admitted == holding`, so a worker holds one entry per stdlib file and not one per
	# fragment), and that a program emits byte-identical Target IR whether it is compiled alone or
	# behind two unrelated compiles in the same process.
	#
	# ⛔ NONE of that is spec-testable, and that is the whole reason for the line: no Maxon program can
	# observe how many times its own stdlib was lexed, and the suite's own green says only that the memo
	# did not break something — never that it FIRED. The gate was written at G20 and this registry entry
	# was not, so for one rung it was 435 lines nobody ran. Requires the shv2 build because it drives
	# that binary and its `verify-warm-rebuild`; it writes only under `mktemp -d` and the gitignored
	# `temp/`.
	"shared-stdlib-memo|shv2|Checking the shared stdlib token memo|bash scripts/shared-stdlib-memo-gate.sh"

	# Needs only the BOOTSTRAP, which is why it does not name the shv2 build: `maxon-dev-mcp/mcp` is
	# compiled by `bin/maxon`. Under the old chain a broken shv2 build silently took the MCP build with
	# it, which is one of the six.
	"mcp-build|bootstrap|Building maxon-dev MCP Server|pkill -f maxon-dev-mcp 2>/dev/null; bin/maxon build maxon-dev-mcp/mcp"

	# `maxon test` discovers the `test` declarations under maxon-dev-mcp/test/, compiles them into one
	# binary with a generated entry point, and runs them — there is no separate runner to build.
	#
	# Requires the SHV2 binary as well as its own: two fixtures shell out to a whole spec-test /
	# scale-test run (protocol.test.maxon's `14-scale-test`), and the scale ladder is shv2's.
	#
	# --timeout bounds a test PROCESS, and a whole test file shares one, so this budget covers all of
	# protocol.test.maxon at once. It is deliberately far larger than the wall time the suite actually
	# takes (single-digit seconds), because of those two fixtures: the number has to be a ceiling on the
	# slowest machine rather than a snug fit on this one.
	"mcp-tests|mcp-build,shv2|Running maxon-dev MCP Tests|bin/maxon test maxon-dev-mcp/test --timeout=1200000"
)

declare -A VERDICT=()
declare -A HEADING=()
ORDER=()

# The prerequisite that stopped this step, or empty when every one of them passed. Returns the FIRST
# blocker by name: a skip has to say which step it is waiting on, or the summary reports an absence
# with no cause and the reader goes looking in the wrong place.
#
# ⛔ A MISORDERED TABLE IS REPORTED THROUGH THE EXIT STATUS, NEVER BY CALLING `exit` HERE. This runs
# inside `$( )`, so `exit` would end the SUBSHELL and nothing else: the caller would read an empty
# blocker — "nothing is stopping this step" — and run it anyway, with the whole run still exiting 0.
# MEASURED 2026-08-06 (BATCH29 review) on the first version of this function. A guard that cannot stop
# the run is the exact shape this script exists to remove, arriving inside the removal.
TABLE_MISORDERED=2
blocked_by() {
	local requires="$1" key
	[ "$requires" = "-" ] && return 0
	local IFS=,
	for key in $requires; do
		# A prerequisite that has not run yet is not a skip: reporting it as one would silently disable
		# every step behind it, which is the failure this whole file is about.
		if [ -z "${VERDICT[$key]+set}" ]; then
			echo "buildall.sh: step '$key' is required before it runs — the STEPS table is misordered." >&2
			return $TABLE_MISORDERED
		fi
		if [ "${VERDICT[$key]}" != "PASS" ]; then
			printf '%s' "$key"
			return 0
		fi
	done
	return 0
}

for row in "${STEPS[@]}"; do
	IFS='|' read -r key requires heading command <<< "$row"
	ORDER+=("$key")
	HEADING[$key]="$heading"

	blocker="$(blocked_by "$requires")"
	# `$?` after an assignment from a command substitution is the SUBSTITUTION's status — the only
	# channel a subshell has back to here, and why `blocked_by` reports a misordered table this way.
	[ $? -eq $TABLE_MISORDERED ] && exit $TABLE_MISORDERED

	if [ -n "$blocker" ]; then
		echo ""
		echo "=== $heading — SKIPPED ==="
		echo "'${HEADING[$blocker]}' did not pass, so there is nothing here to run against."
		VERDICT[$key]="SKIPPED"
		continue
	fi

	echo ""
	echo "=== $heading ==="
	if eval "$command"; then
		VERDICT[$key]="PASS"
	else
		VERDICT[$key]="FAIL"
	fi
done

# Every step and its verdict, PASS included. A summary that listed only the failures would answer
# "what went wrong" but not "what actually ran", and "what actually ran" is the question this script
# spent five days answering wrongly.
echo ""
echo "=== Summary ==="
exit_code=0
for key in "${ORDER[@]}"; do
	verdict="${VERDICT[$key]}"
	printf '  %-8s %s\n' "$verdict" "${HEADING[$key]}"
	[ "$verdict" = "PASS" ] || exit_code=1
done

echo ""
if [ $exit_code -eq 0 ]; then
	echo "All steps completed successfully"
else
	echo "NOT GREEN — see the verdicts above. Every independent gate above ran; a SKIPPED one could not."
fi
exit $exit_code
