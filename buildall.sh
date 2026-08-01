#!/usr/bin/env bash
set -e

echo "=== Building C# Compiler ==="
dotnet build maxon-sharp

echo ""
echo "=== Running C# Spec Tests ==="
bin/maxon spec-test

echo ""
echo "=== Checking Debugger Goldens ==="
# Gates the debugger/profiler/coverage sample transcripts, which the spec suite does not cover:
# their acceptance is a golden transcript, not a spec test. Runs here because it needs only the
# bootstrap — placing it before the (long) shv2 build means a drifted transcript fails fast.
bash scripts/check-debug-goldens.sh

echo ""
echo "=== Building shv2 Compiler ==="
bin/maxon build maxon-shv2

echo ""
echo "=== Running shv2 Spec Tests ==="
maxon-shv2/.maxon/maxon-shv2 spec-test

echo ""
echo "=== Checking the output-lock refusal (E6002) ==="
# Gates what E6002 SAYS, which the spec suite structurally cannot reach: taking that branch needs the
# filesystem to refuse an unlink, and a spec's directive set cannot change a permission. The suite runs
# the success arm thousands of times per run and the refusal arm never — coverage that looks incidental
# rather than absent, which is the worst shape a branch can have.
#
# Runs AFTER the shv2 build because it drives that binary. It deliberately makes a build FAIL and
# asserts the wording of the refusal, so a failure here is the gate working, not the tree being broken.
bash scripts/output-lock-gate.sh

echo ""
echo "=== Checking the stale-binary refusal ==="
# Gates the OTHER half of the same hazard: `output-lock-gate.sh` proves a build that cannot replace
# its output says so, and this proves the harness refuses to report a verdict off the binary such a
# build leaves behind. Neither is spec-testable — no Maxon program can make the binary compiling it
# older than the tree around it — and the property is worth exactly as much as the check nobody runs.
#
# Runs AFTER the shv2 build and suite because it drives that binary, and it ages a source's MTIME
# forward and puts it back; no file's bytes are touched, so the working tree is unchanged either way.
bash scripts/stale-binary-gate.sh

echo ""
echo "=== Building maxon-dev MCP Server ==="
pkill -f maxon-dev-mcp 2>/dev/null || true
bin/maxon build maxon-dev-mcp/mcp

echo ""
echo "=== Running maxon-dev MCP Tests ==="
# `maxon test` discovers the `test` declarations under maxon-dev-mcp/test/, compiles them into one
# binary with a generated entry point, and runs them — there is no separate runner to build.
#
# --timeout bounds a test PROCESS, and a whole test file shares one, so this budget covers all of
# protocol.test.maxon at once. It is deliberately far larger than the wall time the suite actually
# takes (single-digit seconds): two of those fixtures shell out to a whole spec-test / scale-test
# run, so the number has to be a ceiling on the slowest machine rather than a snug fit on this one.
bin/maxon test maxon-dev-mcp/test --timeout=1200000

echo ""
echo "=== All steps completed successfully ==="
