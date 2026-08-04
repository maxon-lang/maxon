#!/usr/bin/env bash
#
# THE CROSS-TARGET GATE — the last thing a rung does before it lands.
#
# Step 8's battery proves the rung on ONE target: whichever one this host happens to be. Everything
# else the compiler claims to emit goes untested until somebody, somewhere, eventually runs it. This
# closes that gap by running every supported target that can be reached from here, and by SAYING SO
# when one cannot.
#
#   target        suite(s)          how it runs                       in the rung gate?
#   ------        --------          -----------                       -----------------
#   x64-windows   C# + shv2         natively                          YES (this IS the host)
#   x64-linux     shv2              WSL2 (static ELF, raw syscalls)   YES, if WSL is installed
#   wasm32-wasi   shv2              vendored wasmtime                 YES, if vendor/wasmtime is present
#   arm64-macos   C# + shv2         ssh -> the Mac, natively          NO — opt in with --mac
#   arm64-linux   shv2              ssh -> the Mac -> OrbStack VM     NO — opt in with --mac
#
# ⭐ THE REMOTE LANES ARE OPT-IN, BECAUSE A REMOTE MACHINE IS NOT A GATE (user, 2026-07-27).
#
# The two arm64 lanes go over `ssh` to a Mac, and everything that makes them slow is the REMOTE part,
# not the arm64 part: a bundle transport, a second checkout's build, an OrbStack guest, and a machine
# that can be asleep, wedged, or on the other side of flaky mDNS. Measured, they cost the rung more
# than they were catching — one wedged `orb run` preflight alone burned ~95 minutes and produced no
# verdict at all. So they are no longer part of the per-rung gate. **They are SYNCED PERIODICALLY, BY
# HAND** — `scripts/cross-target-gate.sh --mac` (or `remote-mac.sh` directly) — and when they run,
# they run under exactly the same rules as before: ran-and-failed is RED.
#
# ⚠ That is a deliberate COVERAGE TRADE, not a claim the arm64 targets are fine. Their rows still
# print, as SKIP with the reason, so a run of this gate can never be mistaken for full coverage.
#
# ⭐ BEST EFFORT MEANS UNREACHABLE IS NOT FAILURE — AND IS NOT SUCCESS EITHER.
#
# A target whose runner is absent is SKIPPED and the gate still passes: a laptop asleep in another
# room must not block a rung. But a skip is REPORTED, never silently folded into the green, because
# the one thing worse than not testing arm64 is believing you did. The matrix prints one row per
# target with its verdict, and the summary counts skips out loud.
#
# A target that RUNS and FAILS is a red gate — that is a rung-halting condition (see the rung
# skill's HALT list), and no flag softens it.
#
# ⚖ AND SINCE 2026-08-02, A RED LANE IS A REAL FAILURE AND CANNOT BE ANYTHING ELSE.
#
# A suite run exits non-zero ONLY for a wrong exit code, wrong stdout, a diagnostic that did not match,
# a compile that should have succeeded, or a leak (101). A committed golden that no longer matches what
# the compiler emits is REPORTED by the run and contributes nothing to its exit code or its failed
# count (user ruling: "the goldens are NOT supposed to be a gate, they are just for reference" — see
# maxon-shv2/Testing/GoldenTracking.maxon).
#
# ⛔ THIS SCRIPT'S RED WAS ONCE READ AS BOOKKEEPING, AND THAT IS THE REASON FOR THE RULING. An
# x64-linux red here was filed as "10 stale golden mismatches + 9 others"; the 9 were nine float
# programs exiting 1 on that target (PLAN row X5), and they went unlooked-at for a day because ten
# pieces of golden bookkeeping in the same list looked exactly as red as they did. ⇒ A FAIL row below
# now means a program did the wrong thing. Read the log; there is nothing in it to regenerate away.
#
# ⭐ THE RUNG PATH DOES NOT REDO WHAT STEP 8 JUST DID (2026-07-27).
#
# Run straight, this script rebuilds both compilers and runs the HOST suite — all three of which the
# rung's own step-8 battery performed moments earlier, on the identical tree. Measured on that tree:
# `dotnet build` + `maxon build maxon-shv2` (13 s) + the host suite (17 s) against a whole local
# matrix of ~2.5 min. That is most of the gate spent re-deriving a known answer.
#
# So the rung path passes `--skip-build --skip-host`, and NEITHER weakens the matrix:
#
#   --skip-build  refuses outright if a SOURCE IS NEWER than the binary it would have built. It does
#                 not trust you; it checks. (Measured 2026-07-27: a stale `maxon-shv2.exe` on a clean
#                 tree read 71 FAILED, and a 13 s rebuild read 1922/0. A flag that merely believed
#                 the caller would have shipped that.)
#   --skip-host   prints the host row as PRIOR, not SKIP — the lane WAS verified, by step 8, on this
#                 tree. SKIP means unverified and inflates the skip count; PRIOR means covered
#                 elsewhere and names by what. Conflating them would be this repo's own signature
#                 bug: one fact, two spellings.
#
# Usage:
#   scripts/cross-target-gate.sh [--filter=PAT] [--csharp]
#                                [--skip-build] [--skip-host]
#                                [--mac] [--mac-host=user@host] [--require-mac]
#
# --mac turns the two remote arm64 lanes back on — that is the periodic manual sync. --mac-host= and
# --require-mac each imply it, since naming a host or demanding it is already asking for the run.
# --require-mac additionally makes an UNREACHABLE Mac a FAILURE rather than a skip, which is what you
# want on a sync run: the whole point of the invocation was to reach it.
#
# --csharp additionally runs the (slow, ~3100-case) C# bootstrap suite on every host that can. Turn
# it on when the rung touched `maxon-sharp/`; the rung's own gate battery already says to.
#
# EXIT: 0 if every target that ran passed (skips allowed) · 1 if any target that ran failed.

# NOT `set -e`: a best-effort matrix must survive one target failing and keep going to the next.
set -uo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"

FILTER=""
# Same fallback `remote-mac.sh` carries: once you have asked for the remote lanes, the host should
# not ALSO have to be spelled out. $MAXON_MAC_HOST and --mac-host= both override it. (It no longer
# decides WHETHER the lanes run — --mac does. A default host used to be the thing standing between
# the arm64 lanes and a silent skip; now the skip is the documented default and the flag is the ask.)
MAC_HOST="${MAXON_MAC_HOST:-estern@Joyces-Macbook-Pro.local}"
REQUIRE_MAC=0
# OFF by default: the remote lanes are the periodic manual sync, not a per-rung gate. See the header.
RUN_MAC=0
RUN_CSHARP=0
# Both OFF by default: run straight, this script is self-contained and assumes nothing was built or
# run before it. The rung path turns them on because step 8 did both. See the header.
SKIP_BUILD=0
SKIP_HOST=0

for arg in "$@"; do
	case "$arg" in
		--filter=*)   FILTER="${arg#*=}" ;;
		--mac)        RUN_MAC=1 ;;
		--mac-host=*) MAC_HOST="${arg#*=}"; RUN_MAC=1 ;;
		--require-mac) REQUIRE_MAC=1; RUN_MAC=1 ;;
		--csharp)     RUN_CSHARP=1 ;;
		--skip-build) SKIP_BUILD=1 ;;
		--skip-host)  SKIP_HOST=1 ;;
		*) echo "cross-target-gate: unknown argument '$arg'" >&2; exit 2 ;;
	esac
done

# shellcheck source=lib/host-binaries.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/host-binaries.sh" || { echo "cannot source scripts/lib/host-binaries.sh" >&2; exit 2; }
IS_WINDOWS="$MAXON_HOST_IS_WINDOWS"

MAXON="$(maxon_bootstrap_path .)"
SHV2="$(maxon_shv2_path .)"
# ⚠ On a Mac the extensionless `wasmtime` beside `wasmtime.exe` is the Mach-O one, so this is the same
#   host-suffix fact and not a coincidence of naming.
WASMTIME="./vendor/wasmtime/wasmtime${MAXON_EXE_EXT}"

SPEC_FILTER=()
[ -n "$FILTER" ] && SPEC_FILTER+=("--filter=$FILTER")

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
# Same contract the maxon-dev MCP server holds over its own binary: a tool that answers confidently
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
	newer="$(find "$@" -type f \( -name '*.maxon' -o -name '*.cs' \) -newer "$binary" -print -quit 2>/dev/null)"

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

# --- Build once. Every local target runs the SAME two binaries; only `--target` differs. ---
if [ "$SKIP_BUILD" = 1 ]; then
	banner "Build SKIPPED (--skip-build) — verifying the existing binaries are not stale"

	assert_fresh "$MAXON" "the bootstrap" maxon-sharp
	assert_fresh "$SHV2" "shv2" maxon-shv2 stdlib

	echo "Both binaries are newer than every source under maxon-sharp/, maxon-shv2/ and stdlib/."
else
	banner "Building (bootstrap, then shv2)"

	if ! dotnet build maxon-sharp; then
		echo "cross-target-gate: the bootstrap failed to build — nothing downstream can be trusted." >&2
		row "ALL" "FAIL" "bootstrap build failed"
		printf '%s\n' "${ROWS[@]}"
		exit 1
	fi

	if ! "$MAXON" build maxon-shv2; then
		echo "cross-target-gate: shv2 failed to build." >&2
		row "ALL" "FAIL" "shv2 build failed"
		printf '%s\n' "${ROWS[@]}"
		exit 1
	fi
fi

# --- x64-windows / the host's own target ---
HOST_TARGET="x64-windows"
[ "$IS_WINDOWS" = 0 ] && HOST_TARGET="$(uname -m)-host"

if [ "$SKIP_HOST" = 1 ]; then
	banner "$HOST_TARGET (native) — PRIOR (--skip-host)"
	echo "The host lane is the one target the rung's step-8 battery already proved, on this tree."
	prior_row "$HOST_TARGET" "shv2 suite — covered by the step-8 battery"
	[ "$RUN_CSHARP" = 1 ] && prior_row "$HOST_TARGET/csharp" "C# suite — covered by the step-8 battery"
else
	banner "$HOST_TARGET (native) — shv2 suite"
	if "$SHV2" spec-test ${SPEC_FILTER[@]+"${SPEC_FILTER[@]}"}; then
		row "$HOST_TARGET" "PASS" "shv2 suite"
	else
		fail_row "$HOST_TARGET" "shv2 suite (exit $?)"
	fi

	if [ "$RUN_CSHARP" = 1 ]; then
		banner "$HOST_TARGET (native) — C# bootstrap suite"
		if "$MAXON" spec-test ${SPEC_FILTER[@]+"${SPEC_FILTER[@]}"}; then
			row "$HOST_TARGET/csharp" "PASS" "C# suite"
		else
			fail_row "$HOST_TARGET/csharp" "C# suite (exit $?)"
		fi
	fi
fi

# --- x64-linux, via WSL ---
#
# `MSYS_NO_PATHCONV=1` because Git Bash rewrites anything that looks like a Unix path into a Windows
# one before the process ever sees it — `/bin/true` arrives as `C:/Program Files/Git/usr/bin/true`,
# which WSL cannot execute. Measured: the probe failed for exactly this reason and said "WSL FAIL"
# on a box where WSL works fine.
banner "x64-linux (WSL) — shv2 suite"
if [ "$IS_WINDOWS" = 1 ] && MSYS_NO_PATHCONV=1 wsl -e /bin/true >/dev/null 2>&1; then
	if "$SHV2" spec-test --target=x64-linux ${SPEC_FILTER[@]+"${SPEC_FILTER[@]}"}; then
		row "x64-linux" "PASS" "shv2 suite via WSL"
	else
		fail_row "x64-linux" "shv2 suite via WSL (exit $?)"
	fi
else
	echo "WSL is not available — skipping x64-linux."
	skip_row "x64-linux" "no WSL on this host"
fi

# --- wasm32-wasi, via the vendored wasmtime ---
banner "wasm32-wasi (wasmtime) — shv2 suite"
if [ -x "$WASMTIME" ]; then
	if "$SHV2" spec-test --target=wasm32-wasi ${SPEC_FILTER[@]+"${SPEC_FILTER[@]}"}; then
		row "wasm32-wasi" "PASS" "shv2 suite via wasmtime"
	else
		fail_row "wasm32-wasi" "shv2 suite via wasmtime (exit $?)"
	fi
else
	echo "No vendored wasmtime at $WASMTIME — skipping wasm32-wasi."
	skip_row "wasm32-wasi" "vendor/wasmtime missing"
fi

# --- arm64-macos + arm64-linux, via ssh to the Mac ---
#
# Delegated wholesale to remote-mac.sh, which owns everything about reaching that machine: the
# availability split, the bundle transport, the OrbStack preflight, the restore-what-we-touched
# contract. This script only needs its VERDICT — which is why that one prints a RESULT line, since
# its exit code deliberately cannot distinguish "asleep" (0) from "green" (0).
banner "arm64-macos + arm64-linux (ssh -> Mac)"
if [ "$RUN_MAC" = 0 ]; then
	echo "Remote lanes are opt-in and were not requested — skipping both arm64 targets."
	echo "They are synced periodically by hand: scripts/cross-target-gate.sh --mac"
	skip_row "arm64-macos" "remote, not requested (--mac)"
	skip_row "arm64-linux" "remote, not requested (--mac)"
elif [ -z "$MAC_HOST" ]; then
	echo "No Mac host configured (--mac-host= or \$MAXON_MAC_HOST) — skipping both arm64 targets."
	skip_row "arm64-macos" "no host configured"
	skip_row "arm64-linux" "no host configured"
else
	MAC_ARGS=(--host="$MAC_HOST" --shv2 --linux)
	[ -n "$FILTER" ] && MAC_ARGS+=("--filter=$FILTER")
	[ "$REQUIRE_MAC" = 1 ] && MAC_ARGS+=(--require-host)

	MAC_LOG="${TMPDIR:-/tmp}/maxon-remote-mac/gate-mac.log"
	mkdir -p "$(dirname "$MAC_LOG")"

	bash scripts/remote-mac.sh "${MAC_ARGS[@]}" 2>&1 | tee "$MAC_LOG"

	# The LAST RESULT line, so a literal "RESULT=" inside earlier suite output cannot be mistaken
	# for the verdict. Absent entirely = the script died before its own EXIT trap could speak, which
	# is a failure however it happened.
	MAC_RESULT="$(grep -o 'remote-mac: RESULT=[A-Z]*' "$MAC_LOG" | tail -1 | cut -d= -f2)"
	case "${MAC_RESULT:-}" in
		PASS)
			row "arm64-macos" "PASS" "C# + shv2 on the Mac"
			row "arm64-linux" "PASS" "shv2 via OrbStack"
			;;
		SKIP)
			skip_row "arm64-macos" "Mac unreachable"
			skip_row "arm64-linux" "Mac unreachable"
			;;
		*)
			fail_row "arm64-macos" "remote run: ${MAC_RESULT:-no verdict}"
			fail_row "arm64-linux" "remote run: ${MAC_RESULT:-no verdict}"
			;;
	esac
fi

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
	echo "RED — $FAILED target(s) ran and FAILED. This is a rung-halting gate: stop and report."
	echo "Every failure counted here is a REAL one — a wrong exit code, wrong stdout, a failed compile or"
	echo "a leak. Goldens are reference and cannot redden a lane, so there is nothing here to regenerate"
	echo "away: read the suite output above and find out what the program did wrong."
	exit 1
fi

if [ "$SKIPPED" -gt 0 ]; then
	# Stated as a limit on COVERAGE, not as a warning to be scrolled past. The gate passed on what it
	# ran, and the rung's report should carry which targets went unverified. A skip is a skip whether
	# the runner was absent or the lane was deliberately not requested — neither one is evidence.
	echo "GREEN, with $SKIPPED target(s) SKIPPED — not run, so UNVERIFIED, not proven good."
	echo "Say which in the rung report; do not describe this run as full cross-target coverage."
	exit 0
fi

if [ "$PRIOR" -gt 0 ]; then
	# Distinct from the SKIP wording above on purpose: these lanes ARE verified, just not by this
	# run. Saying "every supported target tested" would be true of the tree and false of the run.
	echo "GREEN — every supported target covered ($PRIOR lane(s) PRIOR: proved by the step-8 battery on this tree)."
	exit 0
fi

echo "GREEN — every supported target built and tested."
exit 0
