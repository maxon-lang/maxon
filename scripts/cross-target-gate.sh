#!/usr/bin/env bash
#
# THE CROSS-TARGET GATE — the last thing a change does before it lands.
#
# The gate battery proves the change on ONE target: whichever one this host happens to be. Everything
# else the compiler claims to emit goes untested until somebody, somewhere, eventually runs it. This
# closes that gap by running every supported target that can be reached from here, and by SAYING SO
# when one cannot.
#
#   target        suite     how it runs                       in the land gate?
#   ------        -----     -----------                       -----------------
#   x64-windows   specs/    natively                          YES (this IS the host)
#   x64-linux     specs/    WSL2 (static ELF, raw syscalls)   YES, if WSL is installed
#   wasm32-wasi   specs/    vendored wasmtime                 YES, if vendor/wasmtime is present
#   arm64-macos   —         not run by this gate              NEVER — see below
#   arm64-linux   —         not run by this gate              NEVER — see below
#
# ⛔ THIS GATE DOES NOT RUN THE TWO arm64 LANES. CI runs both on every push; the arm64-macos lane is
# reached by hand through `scripts/mac-host.sh` (see the `compiler-workflow` skill).
#
# ⚠ arm64 is UNTESTED by this gate — not "fine". Both rows still print, as SKIP, because the one
# failure this script exists to prevent is a green matrix being read as coverage it never had. **Do
# not describe a change as cross-target verified on arm64 on the strength of this gate.**
#
# ⭐ BEST EFFORT MEANS UNREACHABLE IS NOT FAILURE — AND IS NOT SUCCESS EITHER.
#
# A target whose runner is absent is SKIPPED and the gate still passes: a laptop asleep in another
# room must not block a landing. But a skip is REPORTED, never silently folded into the green, because
# the one thing worse than not testing arm64 is believing you did. The matrix prints one row per
# target with its verdict, and the summary counts skips out loud.
#
# A target that RUNS and FAILS is a red gate — a landing-halting condition — and no flag softens it.
#
# ⚖ A RED LANE IS A REAL FAILURE AND CANNOT BE ANYTHING ELSE.
#
# A suite run exits non-zero ONLY for a wrong exit code, wrong stdout, a diagnostic that did not match,
# a compile that should have succeeded, a leak (101), or — in `ir-specs` — a `TargetIr` pin the compiler
# no longer renders. A FAIL row below therefore means a program did the wrong thing or the emitted code
# moved. Read the log.
#
# ⭐ THE LANDING PATH DOES NOT REDO WHAT THE GATE BATTERY JUST DID.
#
# Run straight, this script rebuilds the compiler and runs the HOST suite — both of which the landing's
# own gate battery performs moments earlier, on the identical tree. Against a whole local matrix
# of ~2.5 min, that is most of the gate spent re-deriving a known answer.
#
# So the landing path passes `--skip-build --skip-host`, and NEITHER weakens the matrix:
#
#   --skip-build  refuses outright if a SOURCE IS NEWER than the binary it would have built. It does
#                 not trust you; it checks. (Measured 2026-07-27: a stale `maxon.exe` on a clean
#                 tree read 71 FAILED, and a 13 s rebuild read 1922/0. A flag that merely believed
#                 the caller would have shipped that.)
#   --skip-host   prints the host row as PRIOR, not SKIP — the lane WAS verified, by the gate battery, on this
#                 tree. SKIP means unverified and inflates the skip count; PRIOR means covered
#                 elsewhere and names by what. Conflating them would be this repo's own signature
#                 bug: one fact, two spellings.
#
# Usage:
#   scripts/cross-target-gate.sh [--filter=PAT] [--skip-build] [--skip-host]
#
# EXIT: 0 if every target that ran passed (skips allowed) · 1 if any target that ran failed.

# NOT `set -e`: a best-effort matrix must survive one target failing and keep going to the next.
set -uo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT" || exit 2

FILTER=""
# Both OFF by default: run straight, this script is self-contained and assumes nothing was built or
# run before it. The landing path turns them on because the gate battery did both. See the header.
SKIP_BUILD=0
SKIP_HOST=0

for arg in "$@"; do
	case "$arg" in
		--filter=*)   FILTER="${arg#*=}" ;;
		--skip-build) SKIP_BUILD=1 ;;
		--skip-host)  SKIP_HOST=1 ;;
		*) echo "cross-target-gate: unknown argument '$arg'" >&2; exit 2 ;;
	esac
done

# shellcheck source=lib/host-binaries.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/host-binaries.sh" || { echo "cannot source scripts/lib/host-binaries.sh" >&2; exit 2; }
IS_WINDOWS="$MAXON_HOST_IS_WINDOWS"

MAXON="$(maxon_compiler_path .)"
# ⚠ `scripts/fetch-vendor.sh` places THIS host's build under its natural name, so the host suffix is
#   the whole of it — there is no other platform's binary in that directory to pick by accident.
WASMTIME="./vendor/wasmtime/wasmtime${MAXON_EXE_EXT}"

SPEC_FILTER=()
[ -n "$FILTER" ] && SPEC_FILTER+=("--filter=$FILTER")

# The emitted-code suite, which a `--filter` cannot be applied to: a pattern naming a `specs/` case
# selects nothing there, and a run that selects nothing is refused.
ir_suite() {
	[ -n "$FILTER" ] && return 0
	"$MAXON" spec-test ir-specs "$@"
}

# One row per target: "target|verdict|detail". Printed as a matrix at the end, because a wall of
# suite output is not a report — the question "which targets did we actually cover?" has to be
# answerable at a glance or nobody will ask it.
ROWS=()
FAILED=0
SKIPPED=0
PRIOR=0

row() { ROWS+=("$1|$2|$3"); }

fail_row() {
	row "$1" "FAIL" "$2"
	FAILED=$((FAILED + 1))
}

skip_row() {
	row "$1" "SKIP" "$2"
	SKIPPED=$((SKIPPED + 1))
}

# A lane this run did not execute because something else ALREADY covered this exact tree. It is not
# a SKIP — a skip means UNVERIFIED, and counting a covered lane as one would understate the matrix
# just as badly as folding a real skip into the green overstates it. The detail must name the cover.
prior_row() {
	row "$1" "PRIOR" "$2"
	PRIOR=$((PRIOR + 1))
}

# --- The --skip-build freshness guard ---
#
# Same contract the maxon MCP server holds over its own binary: a tool that answers confidently
# from stale code is worse than one that refuses. `--skip-build` is the only way into this script
# without a build, so it is the only place the check can live.
assert_fresh() {
	local binary="$1" label="$2"
	shift 2

	if [ ! -x "$binary" ]; then
		echo "cross-target-gate: --skip-build, but $label ($binary) does not exist." >&2
		echo "  Drop --skip-build, or build it first." >&2
		exit 2
	fi

	local newer
	newer="$(find "$@" -type f -name '*.maxon' -newer "$binary" -print -quit 2>/dev/null)"

	if [ -n "$newer" ]; then
		echo "cross-target-gate: --skip-build, but $label is STALE — a source is newer than the binary." >&2
		echo "  binary: $binary" >&2
		echo "  newer:  $newer" >&2
		echo "  Drop --skip-build. Every verdict below it would be about code you are not shipping." >&2
		exit 2
	fi
}

banner() {
	echo
	echo "=============================================================="
	echo "  $1"
	echo "=============================================================="
}

# --- Build once. Every local target runs the SAME binary; only `--target` differs. ---
if [ "$SKIP_BUILD" = 1 ]; then
	banner "Build SKIPPED (--skip-build) — verifying the existing binary is not stale"

	# `runtime/` is read on every compile, the compiler's own included, so an edit there dates the
	# binary exactly as one under `stdlib/` does.
	assert_fresh "$MAXON" "the compiler" maxon-bin stdlib runtime

	echo "The compiler is newer than every source under maxon-bin/, stdlib/ and runtime/."
else
	banner "Building the compiler"

	# ⛔ THE COMPILER THAT BUILDS THIS TREE MUST LIVE INSIDE IT — `stdlib/` and its sibling `runtime/` are
	# found by walking up from the EXECUTABLE, so a `maxon` on PATH would compile this checkout against
	# the RELEASE's sources and succeed. The slot binary first, then the seed; never a PATH lookup.
	builder="$(maxon_compiler_path .)"
	if [ ! -x "$builder" ]; then
		builder=".bootstrap/maxon$MAXON_EXE_EXT"
	fi
	if [ ! -x "$builder" ]; then
		echo "cross-target-gate: no compiler to build with — see CONTRIBUTING.md." >&2
		row "ALL" "FAIL" "no seed"
		printf '%s
' "${ROWS[@]}"
		exit 1
	fi

	if ! "$builder" build; then
		echo "cross-target-gate: the compiler failed to build — nothing downstream can be trusted." >&2
		row "ALL" "FAIL" "build failed"
		printf '%s\n' "${ROWS[@]}"
		exit 1
	fi
fi

# --- x64-windows / the host's own target ---
HOST_TARGET="x64-windows"
[ "$IS_WINDOWS" = 0 ] && HOST_TARGET="$(uname -m)-host"

if [ "$SKIP_HOST" = 1 ]; then
	banner "$HOST_TARGET (native) — PRIOR (--skip-host)"
	echo "The host lane is the one target the gate battery already proved, on this tree."
	prior_row "$HOST_TARGET" "suite — covered by the gate battery"
else
	banner "$HOST_TARGET (native) — suite"
	if "$MAXON" spec-test ${SPEC_FILTER[@]+"${SPEC_FILTER[@]}"} && ir_suite; then
		row "$HOST_TARGET" "PASS" "suite"
	else
		fail_row "$HOST_TARGET" "suite (exit $?)"
	fi
fi

# --- x64-linux, via WSL ---
#
# `MSYS_NO_PATHCONV=1` because Git Bash rewrites anything that looks like a Unix path into a Windows
# one before the process ever sees it — `/bin/true` arrives as `C:/Program Files/Git/usr/bin/true`,
# which WSL cannot execute. Measured: the probe failed for exactly this reason and said "WSL FAIL"
# on a box where WSL works fine.
banner "x64-linux (WSL) — suite"
if [ "$IS_WINDOWS" = 1 ] && MSYS_NO_PATHCONV=1 wsl -e /bin/true >/dev/null 2>&1; then
	if "$MAXON" spec-test --target=x64-linux ${SPEC_FILTER[@]+"${SPEC_FILTER[@]}"} && ir_suite --target=x64-linux; then
		row "x64-linux" "PASS" "suite via WSL"
	else
		fail_row "x64-linux" "suite via WSL (exit $?)"
	fi
else
	echo "WSL is not available — skipping x64-linux."
	skip_row "x64-linux" "no WSL on this host"
fi

# --- wasm32-wasi, via the vendored wasmtime ---
banner "wasm32-wasi (wasmtime) — suite"
if [ -x "$WASMTIME" ]; then
	if "$MAXON" spec-test --target=wasm32-wasi ${SPEC_FILTER[@]+"${SPEC_FILTER[@]}"}; then
		row "wasm32-wasi" "PASS" "suite via wasmtime"
	else
		fail_row "wasm32-wasi" "suite via wasmtime (exit $?)"
	fi
else
	echo "No vendored wasmtime at $WASMTIME — skipping wasm32-wasi."
	skip_row "wasm32-wasi" "vendor/wasmtime missing"
fi

# --- arm64-macos + arm64-linux: not run here ---
#
# The rows are kept, and kept SKIP, on purpose: dropping them would shrink the matrix to the targets
# that happen to be testable here, and a matrix that only lists what it can do is one nobody can
# read a gap out of.
banner "arm64-macos + arm64-linux"
echo "This gate does not run the arm64 lanes. CI runs both on every push to main or pull"
echo "request that changes a CI input; arm64-macos can be hosted by hand through"
echo "scripts/mac-host.sh (see the compiler-workflow skill)."
echo "arm64 is UNVERIFIED by this run, not verified-good. Do not report a change as"
echo "arm64-clean on its strength."
skip_row "arm64-macos" "not run by this gate (CI runs it; scripts/mac-host.sh hosts it)"
skip_row "arm64-linux" "not run by this gate (CI runs it)"

# --- The matrix ---
banner "CROSS-TARGET MATRIX"
printf '%-22s %-6s %s\n' "TARGET" "RESULT" "DETAIL"
printf '%-22s %-6s %s\n' "----------------------" "------" "----------------------------------"
for entry in "${ROWS[@]}"; do
	IFS='|' read -r t v d <<< "$entry"
	printf '%-22s %-6s %s\n' "$t" "$v" "$d"
done

echo
if [ "$FAILED" -gt 0 ]; then
	echo "RED — $FAILED target(s) ran and FAILED. This is a landing-halting gate: stop and report."
	echo "Every failure counted here is a REAL one — a wrong exit code, wrong stdout, a failed compile, a"
	echo "leak or a Target IR pin that no longer matches: read the suite output above and find out what"
	echo "the program did wrong or what the emitted code now says."
	exit 1
fi

if [ "$SKIPPED" -gt 0 ]; then
	# Stated as a limit on COVERAGE, not as a warning to be scrolled past. The gate passed on what it
	# ran, and the landing's report should carry which targets went unverified. A skip is a skip whether
	# the runner was absent or the lane was deliberately not requested — neither one is evidence.
	echo "GREEN, with $SKIPPED target(s) SKIPPED — not run, so UNVERIFIED, not proven good."
	echo "Say which in the landing report; do not describe this run as full cross-target coverage."
	exit 0
fi

if [ "$PRIOR" -gt 0 ]; then
	# Distinct from the SKIP wording above on purpose: these lanes ARE verified, just not by this
	# run. Saying "every supported target tested" would be true of the tree and false of the run.
	echo "GREEN — every supported target covered ($PRIOR lane(s) PRIOR: proved by the gate battery on this tree)."
	exit 0
fi

echo "GREEN — every supported target built and tested."
exit 0
