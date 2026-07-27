#!/usr/bin/env bash
#
# Drive a networked macOS host to run the spec suites and regenerate the arm64 goldens this
# (non-arm64) host cannot produce — arm64-macos natively, and arm64-linux under OrbStack.
#
# WHY THIS EXISTS. The harness has no remote runner. Its three external runners — wasmtime, WSL,
# OrbStack (SpecTestRunner.maxon:905) — are all LOCAL, and all three work only because the binary
# is already visible to them at the same path. A Mach-O from a non-macOS host is refused outright
# (SpecTestRunner.maxon:924), and the C# suite spawns the produced file directly with no runner
# indirection at all (TestRunner.cs:1093). So the Mac is driven as a WHOLE HOST: it builds, it
# runs, and only the resulting golden PATCH comes back over the wire.
#
# THE ASYMMETRY THAT MOTIVATES IT. `--update-required` runs before any test executes
# (TestRunner.cs:60), and this box cross-compiles clean — so it can already WRITE those goldens.
# What it cannot do is EXECUTE them. A golden regenerated here would be committed on the strength
# of "the compiler said so", which is the green-gate-that-proves-nothing this project refuses. The
# Mac supplies the missing half: a real run.
#
# WHICH SUITE CAN REACH WHICH TARGET — the table this script is built around:
#
#   suite          target        on the Mac?
#   -----          ------        -----------
#   C# (specs/)    arm64-macos   YES — the host target, spawned directly
#   C# (specs/)    arm64-linux   NO  — the bootstrap has PeWriter and MachOWriter and no ELF
#                                      writer at all (0-Compiler.cs:224,236). specs/fragments-
#                                      x64-linux/ came from v1, which no longer builds.
#   shv2           arm64-macos   YES — --shv2
#   shv2           arm64-linux   YES — --linux, executed under OrbStack (linuxRunnerForHost)
#
# Usage:
#   scripts/remote-mac.sh [--host=user@mac] [--repo=~/Dev/maxon] [--filter=PAT]
#                         [--update-required] [--shv2] [--linux]
#                         [--require-host] [--no-collect]
#
# Host and repo may also come from $MAXON_MAC_HOST and $MAXON_MAC_REPO.
#
# EXIT CODES — a build wiring this in reads these:
#   0  ran and passed, OR the Mac was unavailable and the run was SKIPPED (see --require-host)
#   1  a real failure: a suite failed, or the Mac is reachable but misconfigured
#   2  usage error
set -euo pipefail

# ⭐ A MACHINE-READABLE VERDICT, so a caller can tell SKIP from PASS — which the EXIT CODE CANNOT,
# because the graceful path deliberately makes both of them 0. `cross-target-gate.sh` reads this
# line to fill in its matrix; without it, "the Mac was asleep" and "arm64 is green" are the same
# observation, and a build would report coverage it never had.
#
# ⚠ DEFAULTS TO FAIL, and the trap fires on EVERY exit including a crash. Fail-closed is the whole
# point: this script has already died mid-run once (a background bash re-reading a file that had
# been edited underneath it), and the one verdict that must never be produced by an unplanned death
# is a green one.
GATE_RESULT="FAIL"
trap 'echo "remote-mac: RESULT=$GATE_RESULT"' EXIT

HOST="${MAXON_MAC_HOST:-}"
REPO="${MAXON_MAC_REPO:-~/Dev/maxon}"
PORT="${MAXON_MAC_PORT:-22}"
FILTER=""
UPDATE_REQUIRED=0
RUN_SHV2=0
RUN_LINUX=0
COLLECT=1
REQUIRE_HOST=0

for arg in "$@"; do
	case "$arg" in
		--host=*)          HOST="${arg#*=}" ;;
		--repo=*)          REPO="${arg#*=}" ;;
		--port=*)          PORT="${arg#*=}" ;;
		--filter=*)        FILTER="${arg#*=}" ;;
		--update-required) UPDATE_REQUIRED=1 ;;
		--shv2)            RUN_SHV2=1 ;;
		--linux)           RUN_LINUX=1 ;;
		--require-host)    REQUIRE_HOST=1 ;;
		--no-collect)      COLLECT=0 ;;
		*) GATE_RESULT="USAGE"; echo "remote-mac: unknown argument '$arg'" >&2; exit 2 ;;
	esac
done

# An absent host is a CONFIGURATION question, not an availability one — there is no machine to be
# unavailable. It stays fatal even under the graceful path.
if [ -z "$HOST" ]; then
	GATE_RESULT="USAGE"
	echo "remote-mac: no host. Pass --host=user@mac or set MAXON_MAC_HOST." >&2
	exit 2
fi

# Bare `--update-required` rewrites every golden in the suite (~1500 fragments). That is never what
# a targeted regeneration wants, and it is not recoverable by reading the output — it is recoverable
# only by `git checkout`. Refuse it here rather than discover it in the diff.
if [ "$UPDATE_REQUIRED" = 1 ] && [ -z "$FILTER" ]; then
	GATE_RESULT="USAGE"
	echo "remote-mac: --update-required needs --filter=PATTERN — unfiltered it rewrites every golden." >&2
	exit 2
fi

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"
SHA="$(git rev-parse HEAD)"
SHORT_SHA="$(git rev-parse --short HEAD)"

PATCH_DIR="${TMPDIR:-/tmp}/maxon-remote-mac"
mkdir -p "$PATCH_DIR"

# ⭐ TWO TRANSPORTS, BECAUSE THE INTERESTING COMMIT IS USUALLY NOT ON ORIGIN YET.
#
# Requiring the commit to be pushed made this tool useless exactly where a rung wants it: at the END
# of a rung the work is a branch tip in a worktree, gated but deliberately NOT pushed — pushing it
# just to test it would put ungated code on origin to find out whether it deserved to be there.
#
# So when origin already has the commit, the Mac just fetches it. When it does not, the commits are
# shipped as a `git bundle` — origin is never written, and no rung branch leaks into the remote.
# `--not --remotes=origin` sends only what origin lacks, making the bundle a handful of commits
# rather than the repo; the Mac fetches origin first, so every prerequisite is already there.
BUNDLE_LOCAL=""
if [ -n "$(git branch -r --contains "$SHA" 2>/dev/null)" ]; then
	echo "remote-mac: $SHORT_SHA is on origin — the Mac will fetch it."
else
	BUNDLE_LOCAL="$PATCH_DIR/work-$SHORT_SHA.bundle"
	echo "remote-mac: $SHORT_SHA is NOT on origin — shipping it as a bundle (origin is not written)."
	# Bundled by REF (`HEAD`), not by raw sha: a bundle records ref NAMES, and one built from a bare
	# hash carries no ref for the far side to fetch. `git fetch <bundle> HEAD` then brings the
	# objects in, after which the sha itself is checkoutable.
	if ! git bundle create "$BUNDLE_LOCAL" HEAD --not --remotes=origin 2>/dev/null; then
		echo "remote-mac: could not bundle $SHORT_SHA. It shares no history with origin, so there is" >&2
		echo "            no prerequisite the Mac could already have. Fetch origin locally first." >&2
		GATE_RESULT="USAGE"
		exit 2
	fi
fi

LOCAL_DIRTY=""
if ! git diff --quiet HEAD 2>/dev/null; then
	LOCAL_DIRTY=1
	echo "remote-mac: NOTE — the local tree is dirty. The Mac runs HEAD ($SHORT_SHA), NOT your working"
	echo "                  changes. Commit and push them if they are meant to be under test."
fi

# BatchMode turns a missing key into an immediate, legible failure instead of a password prompt that
# hangs forever against a non-interactive stdin. `accept-new` is trust-on-first-use: without it the
# FIRST connection to a host fails under BatchMode, because the fingerprint prompt has no one to ask.
SSH_OPTS=(-o BatchMode=yes -o ConnectTimeout=10 -o StrictHostKeyChecking=accept-new -p "$PORT")

# ⭐ AVAILABILITY IS NOT FAILURE, AND THE TWO MUST NOT SHARE AN EXIT CODE.
#
# A laptop that is asleep, off the network, or simply elsewhere is an EXPECTED condition — a build
# that wires this in should carry on. A Mac that ANSWERS but rejects the key, has no checkout, or
# fails the suite is a DIFFERENT thing entirely: it needs a human, and swallowing it would be the
# false green this project keeps refusing.
#
# The split is drawn at the TCP connection, which is the last point where the two are still
# distinguishable. `ssh` itself collapses them — it exits 255 for a DNS failure, a refused port, a
# timeout AND a rejected key alike — so asking ssh "was it available?" cannot be answered. A bare
# socket open can: it succeeds or it does not, and it knows nothing about credentials.
#
# ⚠ Whatever is skipped is announced in a banner, never inferred from silence. "The arm64 goldens
# were not regenerated" has to be READABLE in the log, or a skipped run is indistinguishable from a
# clean one — which is precisely how a gate stops being evidence.
host_reachable() {
	local target="${HOST##*@}"
	timeout 8 bash -c "exec 3<>/dev/tcp/${target}/${PORT}" 2>/dev/null
}

echo "=== Preflight: $HOST ==="
if ! host_reachable; then
	echo
	echo "################################################################"
	echo "#  SKIPPED — the Mac ($HOST) is not reachable on port $PORT."
	echo "#"
	echo "#  Nothing ran. The arm64 goldens were NOT regenerated and the"
	echo "#  arm64 suites were NOT verified at $SHORT_SHA."
	echo "#"
	echo "#  Asleep, off the network, or Remote Login off. Pass"
	echo "#  --require-host to make this a hard failure instead."
	echo "################################################################"
	echo
	GATE_RESULT="SKIP"
	if [ "$REQUIRE_HOST" = 1 ]; then
		GATE_RESULT="FAIL"
		echo "remote-mac: --require-host was given, so an unavailable Mac is fatal." >&2
		exit 1
	fi
	exit 0
fi

# Reachable but unauthenticated is MISCONFIGURATION, and stays fatal under every flag: the machine
# is right there, so carrying on would hide a broken setup rather than tolerate an absent laptop.
if ! ssh "${SSH_OPTS[@]}" "$HOST" true 2>/dev/null; then
	echo "remote-mac: $HOST answers on port $PORT but key auth failed." >&2
	echo "  * Is this host's public key in the Mac's ~/.ssh/authorized_keys?" >&2
	echo "  * ~/.ssh/id_ed25519.pub here is the key being offered." >&2
	exit 1
fi

if [ -n "$FILTER" ]; then
	echo "remote-mac: NOTE — a FILTERED run's fragment goldens are not authoritative."
	echo "                  The runner batches tests into a shared module and slices the IR per test,"
	echo "                  so literal-pool indices (__str_N, __static_lit_N) depend on WHICH tests are"
	echo "                  in the batch. Only spec sources will be collected; fragments are excluded."
fi

REMOTE_PATCH="/tmp/maxon-remote-mac-${SHORT_SHA}.patch"
REMOTE_BUNDLE=""
if [ -n "$BUNDLE_LOCAL" ]; then
	REMOTE_BUNDLE="/tmp/maxon-remote-mac-${SHORT_SHA}.bundle"
	echo "=== Shipping the bundle ($(wc -c < "$BUNDLE_LOCAL") bytes) ==="
	ssh "${SSH_OPTS[@]}" "$HOST" "cat > '$REMOTE_BUNDLE'" < "$BUNDLE_LOCAL"
fi

REMOTE_ARGS=("$REPO" "$SHA" "$FILTER" "$UPDATE_REQUIRED" "$RUN_SHV2" "$RUN_LINUX" "$REMOTE_PATCH" "$REMOTE_BUNDLE")

REMOTE_LOG="$PATCH_DIR/remote-$SHORT_SHA.log"

# ⭐ THE SCRIPT IS SHIPPED AS A FILE, NOT DOWN STDIN — AND THAT IS A BUG FIX, NOT A STYLE CHOICE.
#
# `ssh host bash -s` feeds the script to the remote bash THROUGH STDIN, which means the script and
# any stdin-reading command inside it are drinking from the same straw. MEASURED: the OrbStack
# preflight `orb run -m maxon-linux true` connects stdin to the VM, so it swallowed the ENTIRE
# REMAINDER OF THIS SCRIPT. Bash hit EOF, exited 0, and printed nothing — a run that never happened,
# reporting success. Only the `--linux` runs vanished, and nothing had been echoed yet because every
# echo lives after that check.
#
# Patching `orb` alone would leave the trap armed for the next stdin-reading tool. Uploading the
# script and running it from a FILE retires the whole class: nothing can consume a file by reading
# stdin. `ssh -n` then points the run's own stdin at /dev/null, so a child that reads it gets EOF
# instead of a mouthful of somebody else's script.
REMOTE_SCRIPT="/tmp/maxon-remote-mac-${SHORT_SHA}.sh"

echo "=== Staging the remote script ==="
ssh "${SSH_OPTS[@]}" "$HOST" "cat > '$REMOTE_SCRIPT'" <<'REMOTE'
set -euo pipefail

REPO="$1"; SHA="$2"; FILTER="$3"; UPDATE_REQUIRED="$4"; RUN_SHV2="$5"; RUN_LINUX="$6"; PATCH_OUT="$7"
BUNDLE="${8:-}"

LINUX_TARGET="arm64-linux"
ORB_MACHINE="maxon-linux"

# `~` inside a positional is a literal tilde — the shell expanded nothing, because it never saw the
# word unquoted. Resolve it against the remote $HOME explicitly.
case "$REPO" in "~"*) REPO="$HOME${REPO#\~}" ;; esac

if [ ! -d "$REPO/.git" ]; then
	echo "remote-mac[mac]: no git checkout at $REPO" >&2
	exit 1
fi
cd "$REPO"

# ⚠ `ssh host 'cmd'` GETS A DIFFERENT PATH THAN A TERMINAL ON THE SAME MAC, and BOTH tools this
# script needs are missing from it. macOS composes the login PATH with `path_helper` reading
# `/etc/paths.d/`, which a non-interactive, non-login shell never runs — so a perfectly-installed
# tool reports "not found". Measured on this host: `command -v dotnet` AND `command -v orb` both
# failed while /usr/local/share/dotnet/dotnet (10.0.201) and /usr/local/bin/orb were both present.
#
# It bites `orb` harder than dotnet, and silently: the shv2 runner resolves `orb` through a PATH
# lookup inside the SPAWN (`Executable.name` -> Subprocess.resolveByName), so an absent `orb` is
# not a setup error this script would report — it is every arm64-linux test failing to run.
#
# ONE probe, both callers: the fact "a non-interactive macOS shell has a truncated PATH" is written
# down once here rather than once per tool.
ensure_on_path() {
	local tool="$1"; shift
	if command -v "$tool" >/dev/null 2>&1; then
		return 0
	fi
	local dir
	for dir in "$@"; do
		if [ -x "$dir/$tool" ]; then
			PATH="$dir:$PATH"
			export PATH
			return 0
		fi
	done
	return 1
}

if ! ensure_on_path dotnet /usr/local/share/dotnet /opt/homebrew/bin "$HOME/.dotnet" /usr/local/bin; then
	echo "remote-mac[mac]: no dotnet on PATH, and none at the usual macOS install locations." >&2
	echo "                 The bootstrap targets net10.0, so it needs the .NET 10 SDK." >&2
	exit 1
fi

# The csproj is `<TargetFramework>net10.0</TargetFramework>`; an older SDK fails deep inside MSBuild
# with a message about the framework rather than about what to install.
if ! dotnet --list-sdks | grep -q '^10\.'; then
	echo "remote-mac[mac]: dotnet is $(dotnet --version), but maxon-sharp targets net10.0. Installed:" >&2
	dotnet --list-sdks >&2
	exit 1
fi

# The arm64-linux prerequisites are checked BEFORE the build, not at the first test: a missing
# OrbStack discovered after a full bootstrap build has cost minutes to say something that was
# knowable up front.
if [ "$RUN_LINUX" = 1 ]; then
	if ! ensure_on_path orb /usr/local/bin /opt/homebrew/bin "$HOME/.orbstack/bin"; then
		echo "remote-mac[mac]: --linux needs OrbStack, and no 'orb' was found." >&2
		echo "                 A $LINUX_TARGET ELF has no other runner on a macOS host." >&2
		exit 1
	fi

	# The SAME probe the runner itself uses (`requireOrbMachineReachable`: `orb run -m <m> true`).
	# Asking the question the way the consumer asks it is what stops this preflight from passing on
	# a machine the actual run then cannot use.
	if ! orb run -m "$ORB_MACHINE" true >/dev/null 2>&1; then
		echo "remote-mac[mac]: OrbStack machine '$ORB_MACHINE' is not runnable." >&2
		orb list >&2 2>/dev/null || true
		exit 1
	fi
fi

# ⚠ THE MAC IS SOMEBODY'S WORKING CHECKOUT, NOT A BUILD SLAVE. Moving its HEAD out from under
# in-progress work would be destroying it to run a test. Refuse, name what is in the way, and let a
# human decide — the one thing this script must never do is resolve that conflict on its own.
#
# This refusal is also what makes the cleanup below SAFE: the tree is provably clean going in, so
# everything dirty on the way out was written by this run and by nothing else.
if [ -n "$(git status --porcelain)" ]; then
	echo "remote-mac[mac]: the checkout at $REPO has uncommitted changes. Refusing to move HEAD." >&2
	git status --short >&2
	exit 1
fi

ORIGINAL_REF="$(git symbolic-ref --quiet --short HEAD || git rev-parse HEAD)"
echo "remote-mac[mac]: was on '$ORIGINAL_REF'; will restore it and clean up on the way out."

# Put the checkout back EXACTLY as found — ref, tracked files, and untracked leftovers. Restoring
# only the ref was not enough: `git checkout` carries modified files across, so a run left the Mac
# dirty with regenerated fragments, which then made the NEXT run refuse to start. `clean -fd`
# without `-x` leaves gitignored build output (bin/maxon, .maxon/) in place, so the next run still
# gets a warm tree.
restore() {
	cd "$REPO"
	git reset --quiet >/dev/null 2>&1 || true
	git checkout --quiet -- . >/dev/null 2>&1 || true
	git clean --quiet -fd >/dev/null 2>&1 || true
	git checkout --quiet "$ORIGINAL_REF" >/dev/null 2>&1 || true
}
trap restore EXIT

git fetch --quiet origin

# The bundle carries only what origin lacks, so origin is fetched FIRST to supply its prerequisites.
# Fetched by ref name (`HEAD`) because that is what a bundle records; the fetch brings the objects
# in, after which the sha is an ordinary checkoutable commit. The bundle file is removed once its
# objects are local — it has no further use, and leaving it would litter /tmp on someone's laptop.
if [ -n "$BUNDLE" ]; then
	if ! git fetch --quiet "$BUNDLE" HEAD; then
		echo "remote-mac[mac]: could not fetch from the bundle at $BUNDLE." >&2
		echo "                 Its prerequisites are commits this checkout's origin does not have." >&2
		rm -f "$BUNDLE"
		exit 1
	fi
	rm -f "$BUNDLE"
fi

git checkout --quiet --detach "$SHA"
echo "remote-mac[mac]: at $(git rev-parse --short HEAD) ($(uname -m) $(sw_vers -productName 2>/dev/null || echo macOS))"

# ⭐ HOLD THE MAC AWAKE FOR THE DURATION. A full suite runs for many minutes with no keyboard or
# mouse activity, which is exactly the profile macOS puts to sleep — and it DID: a run died mid-way
# because the laptop idled out from under it. An ssh session is not activity as far as the idle
# timer is concerned.
#
# `-w $$` ties the assertion to THIS shell's lifetime, so it is released when the run ends however
# it ends — no cleanup path to forget, nothing left holding the machine awake afterwards. It cannot
# defeat a closed lid, which is a hardware sleep, so the mid-run guard downstream stays necessary.
if command -v caffeinate >/dev/null 2>&1; then
	caffeinate -i -w $$ &
	echo "remote-mac[mac]: holding off idle sleep for the duration of this run."
fi

echo "=== [mac] Building the C# bootstrap ==="
dotnet build maxon-sharp

SPEC_ARGS=()
[ -n "$FILTER" ] && SPEC_ARGS+=("--filter=$FILTER")
[ "$UPDATE_REQUIRED" = 1 ] && SPEC_ARGS+=("--update-required")

# Run from the repo ROOT: the shv2 runner resolves `specs-shv2/` against the CWD, so a run started
# anywhere else silently tests a different tree. The C# suite goes FIRST — it regenerates the
# fragments the later suites read.
echo "=== [mac] C# spec suite (host target: arm64-macos) ==="
bin/maxon spec-test ${SPEC_ARGS[@]+"${SPEC_ARGS[@]}"}

# shv2 is BUILT BY the bootstrap, so either shv2 mode needs it — built once, used by both.
if [ "$RUN_SHV2" = 1 ] || [ "$RUN_LINUX" = 1 ]; then
	echo "=== [mac] Building shv2 ==="
	bin/maxon build maxon-shv2
fi

if [ "$RUN_SHV2" = 1 ]; then
	echo "=== [mac] shv2 spec suite (host target: arm64-macos) ==="
	maxon-shv2/.maxon/maxon-shv2 spec-test ${SPEC_ARGS[@]+"${SPEC_ARGS[@]}"}
fi

if [ "$RUN_LINUX" = 1 ]; then
	# Cross-compiled here, executed inside the OrbStack VM — which mirrors the Mac's filesystem at
	# identical real paths, so the ELF the build just wrote needs no copying to be runnable.
	echo "=== [mac] shv2 spec suite (--target=$LINUX_TARGET, via OrbStack '$ORB_MACHINE') ==="
	maxon-shv2/.maxon/maxon-shv2 spec-test "--target=$LINUX_TARGET" ${SPEC_ARGS[@]+"${SPEC_ARGS[@]}"}
fi

# ⭐ THE PATCH IS CUT HERE, INSIDE THE SESSION, WHILE HEAD IS STILL THE COMMIT UNDER TEST.
# Cutting it from a second connection instead was wrong in a way the smoke test could not see: by
# then the trap has restored the Mac's own branch, so the diff would be computed against THAT —
# correct only while the Mac happened to sit on the same commit we asked for, and silently full of
# unrelated changes the moment it did not.
#
# `git add -A` first so NEW fragment files land in the diff: a new test produces a new `.test` file,
# and plain `git diff` cannot see an untracked one.
git add -A

if [ -n "$FILTER" ]; then
	# A filtered run's fragments are batch-dependent noise (see the local NOTE), so only the spec
	# SOURCES — where `--update-required` writes RequiredIR — are worth carrying home.
	git diff --cached --binary -- . ':(exclude)specs/fragments-*' ':(exclude)specs-shv2/fragments' > "$PATCH_OUT"
else
	git diff --cached --binary > "$PATCH_OUT"
fi

echo "=== [mac] Regenerated ==="
git diff --cached --stat

# ⭐ THE POSITIVE COMPLETION MARKER. The local half REQUIRES this line and treats its absence as a
# run that did not finish — see "AN EXIT CODE IS NOT EVIDENCE" there.
echo "remote-mac[mac]: RUN-COMPLETE $SHA"
REMOTE

# Each argument quoted for the remote shell — ssh flattens argv into ONE string handed to the remote
# login shell (which on macOS may be zsh), so anything unquoted is re-split there. The staged script
# is removed whatever the run's exit status, and that status is preserved across the cleanup.
REMOTE_CMD="bash '$REMOTE_SCRIPT'"
for remote_arg in "${REMOTE_ARGS[@]}"; do
	REMOTE_CMD="$REMOTE_CMD $(printf '%q' "$remote_arg")"
done
REMOTE_CMD="$REMOTE_CMD; rc=\$?; rm -f '$REMOTE_SCRIPT'; exit \$rc"

# Tee'd rather than merely streamed, because the completion check below has to READ what the remote
# said. `pipefail` is what keeps the pipeline's status ssh's own rather than tee's.
echo "=== Running on $HOST ==="
REMOTE_STATUS=0
ssh -n "${SSH_OPTS[@]}" "$HOST" "$REMOTE_CMD" 2>&1 | tee "$REMOTE_LOG" || REMOTE_STATUS=$?

# ⭐ AN EXIT CODE IS NOT EVIDENCE THAT A RUN HAPPENED — REQUIRE THE MARKER.
#
# MEASURED, not theorized: the Mac slept mid-run. The reachability probe had already passed (it was
# awake when asked), the connection then died, and the local half sailed on to the collect step and
# reported a baffling `cat: No such file`. The remote had produced NOT ONE LINE of output and the
# status still did not say so.
#
# So completion is established POSITIVELY, by a marker the remote prints as its last act, and not by
# the absence of a complaint. A point-in-time probe can only ever say the host was up WHEN ASKED; it
# cannot promise the host stays up, and nothing checked afterwards.
REMOTE_COMPLETED=0
if grep -q "RUN-COMPLETE $SHA" "$REMOTE_LOG" 2>/dev/null; then
	REMOTE_COMPLETED=1
fi

# ssh reserves 255 for its OWN transport failures — a dropped connection, a host that went away
# mid-session. The remote script never exits 255 itself, so this code is unambiguous here: the Mac
# became unavailable. That is the same CONDITION the preflight banner handles, just discovered
# later, so it gets the same graceful treatment rather than being dressed up as a suite failure.
#
# ⚠ GATED ON 255 ALONE, and an empty log is deliberately NOT part of the test. It was, and that was
# wrong: a silent run is not evidence of an absent host. When the OrbStack preflight ate this
# script through stdin, the result was exit 0 with no output, and treating "no output" as
# unavailability reported a SKIP for a real, reproducible bug — the graceful path laundering a
# defect into a shrug. Exit 0 means the transport was fine, so whatever went wrong is ours.
SSH_TRANSPORT_FAILURE=255
if [ "$REMOTE_COMPLETED" = 0 ] && [ "$REMOTE_STATUS" = "$SSH_TRANSPORT_FAILURE" ]; then
	echo
	echo "################################################################"
	echo "#  INCOMPLETE — the Mac went away mid-run (ssh exit $REMOTE_STATUS)."
	echo "#"
	echo "#  It was reachable at preflight, so the run STARTED; it did not"
	echo "#  finish, and no goldens were collected. Nothing was applied."
	echo "#"
	echo "#  Most likely it went to sleep. Re-run when it is awake."
	echo "################################################################"
	echo
	GATE_RESULT="SKIP"
	if [ "$REQUIRE_HOST" = 1 ]; then
		GATE_RESULT="FAIL"
		echo "remote-mac: --require-host was given, so a lost Mac is fatal." >&2
		exit 1
	fi
	exit 0
fi

# Output, but no marker: the remote reached one of its own refusals (dirty checkout, missing SDK, a
# failing suite) or died partway. Either way it is a REAL failure — the machine was there and
# something about the run was wrong. Never graceful, whatever the flags say.
if [ "$REMOTE_COMPLETED" = 0 ]; then
	echo
	echo "=== The remote run did NOT complete (ssh exit $REMOTE_STATUS) ==="
	echo "No RUN-COMPLETE marker. The log above is the whole story; nothing was applied."
	echo "Full log: $REMOTE_LOG"
	exit 1
fi

if [ "$REMOTE_STATUS" != 0 ]; then
	echo
	echo "=== The remote run FAILED (exit $REMOTE_STATUS) ==="
	# A failing suite is the case where the diff is most worth LOOKING at and least safe to trust:
	# whatever it regenerated, it did so alongside something that did not pass. So it is still
	# fetched — a partial regeneration is evidence — but never applied on its own recognizance.
	echo "Fetching the patch anyway, for inspection. It will NOT be applied."
fi

if [ "$COLLECT" = 0 ]; then
	echo "=== Done (--no-collect: the patch stays at $REMOTE_PATCH on the Mac) ==="
	if [ "$REMOTE_STATUS" = 0 ]; then GATE_RESULT="PASS"; fi
	exit "$REMOTE_STATUS"
fi

PATCH="$PATCH_DIR/goldens-$SHORT_SHA.patch"

echo "=== Collecting the golden patch ==="
COLLECT_STATUS=0
ssh "${SSH_OPTS[@]}" "$HOST" "cat '$REMOTE_PATCH' && rm -f '$REMOTE_PATCH'" > "$PATCH" 2>/dev/null || COLLECT_STATUS=$?

# The run COMPLETED, so the patch was written; failing to read it back now is a second-connection
# problem (the Mac slept in the gap), not a missing result. Say which it is — the earlier version
# let `set -e` abort on a raw `cat:` error that named neither cause.
if [ "$COLLECT_STATUS" != 0 ]; then
	echo "remote-mac: the run finished, but the patch could not be read back from $HOST:$REMOTE_PATCH" >&2
	echo "            (ssh exit $COLLECT_STATUS). It is still on the Mac — re-collect with:" >&2
	echo "            ssh $HOST cat '$REMOTE_PATCH' > local.patch" >&2
	rm -f "$PATCH"
	exit 1
fi

if [ ! -s "$PATCH" ]; then
	echo "No golden churn — the Mac's tree is byte-identical to $SHORT_SHA. Nothing to apply."
	rm -f "$PATCH"
	if [ "$REMOTE_STATUS" = 0 ]; then GATE_RESULT="PASS"; fi
	exit "$REMOTE_STATUS"
fi

echo "Patch: $PATCH"
git apply --stat "$PATCH"

if [ "$REMOTE_STATUS" != 0 ]; then
	echo
	echo "NOT applied — the run that produced it failed. Inspect it, fix the failure, re-run:"
	echo "    git apply --3way '$PATCH'"
	exit "$REMOTE_STATUS"
fi

if [ -n "$LOCAL_DIRTY" ]; then
	echo
	echo "Local tree is dirty — NOT applying automatically. Review, then:"
	echo "    git apply --3way '$PATCH'"
	# The RUN is what this verdict reports, and the run passed. Not applying is a local choice about
	# a dirty tree, not a failure of the thing being gated.
	GATE_RESULT="PASS"
	exit 0
fi

# `--3way` STAGES what it applies, so the result shows under `git diff --cached`, not `git diff`.
git apply --3way "$PATCH"
echo
echo "Applied (and STAGED — see 'git diff --cached'). Review it: a moved fragment is a codegen"
echo "change, so commit the churn rather than reverting it."
git status --short
GATE_RESULT="PASS"
