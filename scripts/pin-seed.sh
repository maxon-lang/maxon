#!/usr/bin/env bash
#
# Land a commit the pinned seed refuses, with a seed frozen from it. Run in place of `git push`, after the
# landing gates and the commit: it freezes `HEAD` with `scripts/freeze-seed.sh`, proves the seed, publishes it
# as a `seed-*` pre-release, and pushes `HEAD` and the commit pinning it to `main` in one push. The host's lane
# is always proved; each `--prove=<lane>` is proved when it is reachable (x64-linux through WSL, arm64-macos on
# the shared Mac) and left to CI with a notice when it is not. A failure after anything left the machine
# retracts what this run published and undoes the pin commit.
#
# `docs/RELEASING.md` owns the procedure ("A commit the pinned seed refuses lands with its own seed") and the
# choice of lanes ("Which lanes to prove").
#
# Usage:
#   scripts/pin-seed.sh                              # tag seed-<today>-<first 12 of HEAD>
#   scripts/pin-seed.sh --date=2026-10-14            # tag seed-2026-10-14-<first 12 of HEAD>
#   scripts/pin-seed.sh --prove=x64-linux --prove=arm64-macos

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/host-binaries.sh
. scripts/lib/seed.sh

Remote="origin"
Branch="main"
MaxFetchAttempts=3
FetchRetrySeconds=15
SeedFetchCheckDirectory="$SeedWorkDirectory/fetch-check"
SeedProvePrefix="$SeedWorkDirectory/prove"
SelfCompileOutput="temp/self-compile/maxon"
WindowsHostTarget="x64-windows"
WslLaneTarget="x64-linux"
MacLaneTarget="arm64-macos"
MacHostScript="./scripts/mac-host.sh"
MacHostSkippedNotice="mac-host.sh: skipped"
WslProbeBoundSeconds=60
MacProbeBoundSeconds=8
MacProbeLog="$SeedWorkDirectory/mac-probe.log"
MacCloneLog="$SeedWorkDirectory/mac-clone.log"
MacSessionPrefix="pin"
MacLockWaitSeconds=1800
MacPollSeconds=30
MaxMacPollFailures=5
MacJobBoundSeconds=3600
MacJobAlive="alive"
MacJobFinished="finished"
BoundedPollSeconds=1
BoundedKillGraceSeconds=2
ExitSuccess=0
GitNotAncestor=1
RemoteUnreadable="unreadable"
ExitHungUp=129
ExitInterrupted=130
ExitTerminated=143

host_target="$(maxon_host_target)"
date_stamp="$(date +"$SeedDateFormat")"
prove_lanes="$host_target"
reachable_lanes=""
lane_runner=()
lane_skip_reason=""
mac_unreachable_reason=""
bounded_timed_out="no"
tag_created="no"
tag_push_started="no"
release_started="no"
pin_written="no"
mac_session=""
self_compile_command=()

for argument in "$@"; do
	case "$argument" in
		--date=$SeedDatePattern) date_stamp="${argument#--date=}" ;;
		--prove=?*)
			lane="${argument#--prove=}"
			maxon_is_target "$lane" || { echo "pin-seed.sh: $lane is not a native target (one of: $MaxonTargets)" >&2; exit 2; }

			case " $prove_lanes " in
				*" $lane "*) ;;
				*) prove_lanes="$prove_lanes $lane" ;;
			esac
			;;
		*) echo "pin-seed.sh: unknown argument: $argument" >&2; exit 2 ;;
	esac
done

fetch_branch() {
	git fetch --quiet "$Remote" "+refs/heads/$Branch:refs/remotes/$Remote/$Branch"
}

require_rebased() {
	fetch_branch

	git merge-base --is-ancestor "$Remote/$Branch" HEAD \
		|| { echo "pin-seed.sh: $Remote/$Branch is not an ancestor of HEAD — rebase onto it, re-run the gates and re-run this script; nothing was published" >&2; exit 1; }
}

require_pin_file_unchanged() {
	[ -z "$(git status --porcelain -- "$SeedPinFile")" ] \
		|| { echo "pin-seed.sh: $SeedPinFile has an uncommitted change" >&2; exit 1; }
}

require_no_release() {
	if seed_release_published "$tag"; then
		echo "pin-seed.sh: the release $tag already exists — a seed tag never moves" >&2
		exit 1
	fi
}

require_no_tag() {
	local remote_tag

	remote_tag="$(git ls-remote --tags "$Remote" "refs/tags/$tag")"
	[ -z "$remote_tag" ] || { echo "pin-seed.sh: $Remote already has the tag $tag — a seed tag never moves" >&2; exit 1; }

	if git rev-parse --quiet --verify "refs/tags/$tag" > /dev/null; then
		echo "pin-seed.sh: this checkout already has the tag $tag — a seed tag never moves" >&2
		exit 1
	fi
}

require_unpublished() {
	require_no_release
	require_no_tag
}

require_still_unpublished() {
	require_rebased
	require_no_release

	if [ "$tag_push_started" = "no" ]; then
		require_no_tag
	fi
}

proves_through_mac_host() {
	[ "$1" = "$MacLaneTarget" ] && [ "$1" != "$host_target" ]
}

# macOS has no `timeout`, and this script runs on any native host.
run_bounded() {
	local seconds="$1" pid waited=0
	shift

	bounded_timed_out="no"
	"$@" &
	pid=$!

	while kill -0 "$pid" 2> /dev/null; do
		if [ "$waited" -ge "$seconds" ]; then
			bounded_timed_out="yes"
			kill -TERM "$pid" 2> /dev/null || true
			sleep "$BoundedKillGraceSeconds"
			kill -KILL "$pid" 2> /dev/null || true
			wait "$pid" 2> /dev/null || true
			return 1
		fi

		sleep "$BoundedPollSeconds"
		waited=$((waited + BoundedPollSeconds))
	done

	wait "$pid"
}

mac_host_answers() {
	local status=0 answer

	mkdir -p "$SeedWorkDirectory"

	# Into a file, not `$(...)`: a command substitution's subshell would lose `bounded_timed_out`, and would
	# wait past the bound for any child of the killed probe still holding the pipe.
	run_bounded "$MacProbeBoundSeconds" "$MacHostScript" ping > "$MacProbeLog" 2>&1 < /dev/null || status=$?
	answer="$(cat "$MacProbeLog")"

	if [ "$bounded_timed_out" = "yes" ]; then
		mac_unreachable_reason="$MacHostScript ping did not answer within ${MacProbeBoundSeconds}s"
		return 1
	fi

	if [ "$status" -ne "$ExitSuccess" ]; then
		mac_unreachable_reason="$MacHostScript ping failed (exit $status): ${answer##*$'\n'}"
		return 1
	fi

	case "$answer" in
		*"$MacHostSkippedNotice"*)
			mac_unreachable_reason="$answer"
			return 1
			;;
	esac

	return 0
}

select_lane_runner() {
	local lane="$1"

	lane_runner=()
	lane_skip_reason=""

	if [ "$lane" = "$host_target" ]; then
		return 0
	fi

	case "$host_target/$lane" in
		"$WindowsHostTarget/$WslLaneTarget")
			if command -v wsl > /dev/null 2>&1 && run_bounded "$WslProbeBoundSeconds" wsl -- true > /dev/null 2>&1 < /dev/null; then
				lane_runner=(wsl --)
				return 0
			fi

			lane_skip_reason="WSL does not answer on this machine"
			;;
		*"/$MacLaneTarget")
			if mac_host_answers; then
				return 0
			fi

			lane_skip_reason="$mac_unreachable_reason"
			;;
		*)
			lane_skip_reason="a $lane compiler runs on a $lane host only, and this host is $host_target"
			;;
	esac

	return 1
}

plan_proofs() {
	local lane

	reachable_lanes=""

	for lane in $prove_lanes; do
		if select_lane_runner "$lane"; then
			reachable_lanes="$reachable_lanes $lane"
		else
			echo "pin-seed.sh: $lane is not proved before publishing — $lane_skip_reason; CI's $lane lane on the pushed pin is its proof"
		fi
	done
}

mac_proof_planned() {
	local lane

	for lane in $reachable_lanes; do
		if proves_through_mac_host "$lane"; then
			return 0
		fi
	done

	return 1
}

fail_proof() {
	echo "pin-seed.sh: proving $tag on $1 failed at $2 — $tag stays unpublished" >&2
	exit 1
}

prove_step() {
	local directory="$1" lane="$2" description="$3"
	shift 3

	(cd "$directory" && ${lane_runner[@]+"${lane_runner[@]}"} "$@") \
		|| fail_proof "$lane" "$description"
}

set_proof_commands() {
	local exe_ext
	exe_ext="$(maxon_target_exe_ext "$1")"
	self_compile_command=("$(maxon_downloaded_path . "$exe_ext")" build maxon-bin "--output=$SelfCompileOutput$exe_ext")
}

# The first match: `mac-host.sh job` prints its log's tail after the header.
marked_line() {
	printf '%s\n' "$2" | awk -v key="$1=" 'index($0, key) == 1 && !found { print substr($0, length(key) + 1); found = 1 }'
}

drop_mac_session() {
	local key=""

	if [ -f "$MacCloneLog" ]; then
		key="$(marked_line session-key "$(cat "$MacCloneLog")")"
	fi

	if [ -z "$key" ]; then
		echo "pin-seed.sh: the Mac printed no session key for $mac_session, so nothing is dropped — $MacHostScript status lists it if its clone survived" >&2
	else
		"$MacHostScript" drop "$mac_session" "$key" \
			|| echo "pin-seed.sh: the Mac session $mac_session is still there — run: $MacHostScript drop $mac_session $key" >&2
	fi

	mac_session=""
}

await_mac_job() {
	local lane="$1" session="$2" report state failures=0 deadline=$((SECONDS + MacJobBoundSeconds))

	while true; do
		[ "$SECONDS" -lt "$deadline" ] || fail_proof "$lane" "the Mac job (still running after $MacJobBoundSeconds s)"

		if report="$("$MacHostScript" job "$session")"; then
			failures=0
			state="$(marked_line state "$report")"

			if [ "$state" = "$MacJobFinished" ] && [ "$(marked_line exit "$report")" = "$ExitSuccess" ]; then
				printf '%s\n' "$report"
				return 0
			fi

			if [ "$state" != "$MacJobAlive" ]; then
				printf '%s\n' "$report" >&2
				fail_proof "$lane" "the self-compile on the Mac (job ${state:-in no state})"
			fi
		else
			failures=$((failures + 1))
			[ "$failures" -lt "$MaxMacPollFailures" ] || fail_proof "$lane" "polling the Mac job ($failures consecutive failures)"
		fi

		sleep "$MacPollSeconds"
	done
}

prove_on_mac() {
	local lane="$1" session="$MacSessionPrefix-$tag-$$" status=0 output token line

	rm -f "$MacCloneLog"
	mac_session="$session"
	"$MacHostScript" clone "$session" "$sha" "--seed=$tag" "--seed-from=$repo_root/$SeedDistDirectory" > "$MacCloneLog" || status=$?
	cat "$MacCloneLog"

	[ "$status" -eq "$ExitSuccess" ] || fail_proof "$lane" "cloning $sha and installing the frozen seed on the Mac"

	output="$("$MacHostScript" lock "$session" "--wait=$MacLockWaitSeconds" "proving $tag")" \
		|| fail_proof "$lane" "taking the Mac's host lock"
	token="$(marked_line token "$output")"
	[ -n "$token" ] || fail_proof "$lane" "reading the Mac lock's token"

	"$MacHostScript" run "$session" "$token" "${self_compile_command[@]}" \
		|| fail_proof "$lane" "starting the self-compile on the Mac"

	await_mac_job "$lane" "$session"
	drop_mac_session
}

prove_lane() {
	local lane="$1"
	local directory="$SeedProvePrefix-$lane"

	select_lane_runner "$lane" \
		|| { echo "pin-seed.sh: $lane became unreachable — $lane_skip_reason; nothing was published" >&2; exit 1; }

	set_proof_commands "$lane"

	if proves_through_mac_host "$lane"; then
		prove_on_mac "$lane"
		return 0
	fi

	rm -rf "$directory"

	git clone --quiet --no-checkout "$repo_root" "$directory"
	git -C "$directory" checkout --quiet --detach "$sha"

	prove_step "$directory" "$lane" "fetching the frozen seed" bash ./scripts/fetch-seed.sh "$tag" "$lane" --from="../$(basename "$SeedDistDirectory")"
	prove_step "$directory" "$lane" "the frozen seed's self-compile" "${self_compile_command[@]}"

	rm -rf "$directory"
}

undo_pin() {
	if [ "$(git rev-parse HEAD)" != "$sha" ]; then
		if [ "$(git rev-parse HEAD^)" != "$sha" ]; then
			echo "pin-seed.sh: HEAD is no longer the pin commit on $sha — the pin commit is not undone" >&2
			return 0
		fi

		git reset --quiet --soft "$sha" || { echo "pin-seed.sh: undoing the pin commit failed — run: git reset --soft $sha" >&2; return 0; }
	fi

	git checkout "$sha" -- "$SeedPinFile" || echo "pin-seed.sh: restoring $SeedPinFile failed — run: git checkout $sha -- $SeedPinFile" >&2
}

delete_local_tag() {
	if git rev-parse --quiet --verify "refs/tags/$tag" > /dev/null; then
		git tag --delete "$tag" > /dev/null || echo "pin-seed.sh: deleting the local tag $tag failed — run: git tag --delete $tag" >&2
	fi
}

# A release this run did not create, or a remote tag naming another commit, is another run's seed, which a
# pushed pin may already name.
retract_published() {
	local remote_tag

	if seed_release_published "$tag"; then
		if [ "$release_started" = "no" ]; then
			echo "pin-seed.sh: the release $tag exists and this run did not create it — it and its tag are left as they are" >&2
			return 0
		fi

		gh release delete "$tag" --cleanup-tag --yes || return 1
	fi

	remote_tag="$(git ls-remote --tags "$Remote" "refs/tags/$tag")" || return 1

	if [ "${remote_tag%%[[:space:]]*}" = "$sha" ]; then
		git push --quiet "$Remote" ":refs/tags/$tag" || return 1
	fi
}

# A pin that may be on the remote is never retracted: deleting the seed it names would leave `main` pinned to
# a release that does not exist, so nothing built from it can fetch its seed.
pin_may_be_on_remote() {
	local head contained="$ExitSuccess"

	head="$(git rev-parse HEAD)"
	[ "$head" != "$sha" ] || return 1

	if fetch_branch; then
		git merge-base --is-ancestor "$head" "$Remote/$Branch" || contained=$?
	else
		contained="$RemoteUnreadable"
	fi

	case "$contained" in
		"$ExitSuccess")
			echo "pin-seed.sh: $Remote/$Branch has the pin $head — $tag stays published"
			return 0
			;;
		"$GitNotAncestor") return 1 ;;
		*)
			echo "pin-seed.sh: $Remote cannot say whether $Branch took the pin $head — nothing is retracted; if $Remote/$Branch does not contain $head, run: gh release delete $tag --cleanup-tag --yes; git tag --delete $tag; git reset --soft $sha; git checkout $sha -- $SeedPinFile" >&2
			return 0
			;;
	esac
}

retract() {
	if [ "$pin_written" = "yes" ]; then
		if pin_may_be_on_remote; then
			return 0
		fi

		undo_pin
	fi

	if [ "$tag_created" = "yes" ]; then
		echo "pin-seed.sh: retracting $tag" >&2
		delete_local_tag
	fi

	if [ "$tag_push_started" = "yes" ]; then
		( retract_published ) \
			|| echo "pin-seed.sh: retracting $tag from $Remote did not finish — check: gh release view $tag; git ls-remote --tags $Remote refs/tags/$tag" >&2
	fi
}

conclude() {
	local status=$?

	trap '' INT TERM HUP
	trap - EXIT

	if [ -n "$mac_session" ]; then
		drop_mac_session
	fi

	if [ "$status" -ne "$ExitSuccess" ]; then
		retract
	fi

	exit "$status"
}

push_tag() {
	if [ "$tag_push_started" = "yes" ]; then
		return 0
	fi

	tag_created="yes"
	git tag "$tag" "$sha"
	tag_push_started="yes"

	echo "pin-seed.sh: pushing the tag $tag"
	git push --quiet "$Remote" "refs/tags/$tag" \
		|| { echo "pin-seed.sh: $Remote refused the tag $tag" >&2; exit 1; }
}

publish_seed() {
	seed_release_create_command "$tag"
	push_tag
	release_started="yes"

	"${seed_release_create[@]}" \
		|| { echo "pin-seed.sh: creating the release $tag failed" >&2; exit 1; }
}

require_fetchable() {
	local target attempt

	for target in $MaxonTargets; do
		attempt=1

		until ./scripts/fetch-seed.sh "$tag" "$target" --output="$SeedFetchCheckDirectory/$target/maxon"; do
			if [ "$attempt" -ge "$MaxFetchAttempts" ]; then
				echo "pin-seed.sh: $tag is published but its $target seed does not fetch — retracting the release; nothing is pinned" >&2
				exit 1
			fi

			attempt=$((attempt + 1))
			echo "pin-seed.sh: fetching $tag for $target failed — retrying (attempt $attempt of $MaxFetchAttempts)" >&2
			sleep "$FetchRetrySeconds"
		done
	done
}

commit_pin() {
	pin_written="yes"
	printf '%s\n' "$tag" > "$SeedPinFile"

	git commit --quiet -m "The tree is built with $tag, frozen from ${sha:0:$SeedCommitAbbreviation}" -- "$SeedPinFile"
}

push_pin() {
	git push --quiet "$Remote" "HEAD:refs/heads/$Branch" \
		|| { echo "pin-seed.sh: the push to $Remote/$Branch failed — rebase onto $Remote/$Branch, re-run the gates and re-run this script" >&2; exit 1; }
}

sha="$(git rev-parse HEAD)"
tag="$(seed_tag "$date_stamp" "$sha")"

trap conclude EXIT
trap 'exit "$ExitHungUp"' HUP
trap 'exit "$ExitInterrupted"' INT
trap 'exit "$ExitTerminated"' TERM

require_rebased
require_pin_file_unchanged
require_unpublished
plan_proofs

./scripts/freeze-seed.sh --date="$date_stamp"

# The Mac clones from the remote, so `$sha` must be reachable there before the proof: the tag makes it so.
if mac_proof_planned; then
	push_tag
fi

for lane in $reachable_lanes; do
	prove_lane "$lane"
done

require_still_unpublished

echo "pin-seed.sh: publishing $tag"
publish_seed
require_fetchable
commit_pin
push_pin
echo "pin-seed.sh: $Branch is pinned to $tag"
