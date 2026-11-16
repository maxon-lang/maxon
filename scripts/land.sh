#!/usr/bin/env bash
#
# Land a branch on `main`: the steps of `.github/workflows/land.yml`, one subcommand per job, and the local
# entry point that starts it. `docs/RELEASING.md` owns the procedure ("A commit the pinned seed refuses lands
# with its own seed").
#
# Usage:
#   scripts/land.sh submit [--rehearsal]   # push HEAD to land/<first 12 of HEAD>, run land.yml on it, follow it
#   scripts/land.sh chain <base> <head>    # build the bootstrap chain base..head; prints the head's compiler
#   scripts/land.sh in-flight [--wait]     # the chain land in flight, if any: branch and run, or "none"
#   scripts/land.sh plan <head>            # land.yml's jobs; they read LAND_* from the environment
#   scripts/land.sh freeze
#   scripts/land.sh prove <lane>
#   scripts/land.sh publish
#   scripts/land.sh land
#   scripts/land.sh retract

set -euo pipefail
shopt -s inherit_errexit 2> /dev/null || true

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/host-binaries.sh
. scripts/lib/seed.sh

Remote="origin"
Branch="main"
LandWorkflow="land.yml"
LandTrailer="Land"
ChainTrailer="Bootstrap-Chain"
ModeMain="main"
ModeRehearsal="rehearsal"
KindNone="none"
KindOrdinary="ordinary"
KindChain="chain"
VerdictAccepted="accepted"
VerdictRefused="refused"
ChainRefSuffix="-chain"
WalkGuarded="guarded"
WalkUnguarded="unguarded"
RunStatusesInFlight="queued in_progress waiting pending requested"
JobChain="chain"
JobCredential="credential"
JobPlan="plan"
JobSkipped="skipped"
InFlightPollSeconds=60
InFlightNone="none"
SubmitBranchPrefix="land/"
LandWorkDirectory="temp/land-branch"
ChainTree="$LandWorkDirectory/tree"
ChainCompilers="$LandWorkDirectory/compilers"
ProveOutput="$LandWorkDirectory/prove/maxon"
VerdictLog="$LandWorkDirectory/verdict.log"
VerdictTailLines=40
BotName="github-actions[bot]"
BotEmail="41898282+github-actions[bot]@users.noreply.github.com"
UtcOffset="+0000"
MaxFetchAttempts=3
FetchRetrySeconds=15
SubmitPollSeconds=30
SubmitFindAttempts=20
MaxSubmitPollFailures=10
RunCompleted="completed"
RunSuccess="success"

# The trailer key is matched case-insensitively, as git matches trailer keys.
AwkIsLandLine='function is_land(line) { return index(tolower(line), tolower(key) ":") == 1 }'

fail() {
	echo "land.sh: $*" >&2
	exit 1
}

require_env() {
	local name

	for name in "$@"; do
		[ -n "${!name:-}" ] || fail "$name is not set — land.yml passes it from the plan job"
	done
}

output() {
	if [ -n "${GITHUB_OUTPUT:-}" ]; then
		printf '%s=%s\n' "$1" "$2" >> "$GITHUB_OUTPUT"
	fi

	printf 'land.sh: %s=%s\n' "$1" "$2"
}

summary() {
	if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
		printf '%s\n' "$*" >> "$GITHUB_STEP_SUMMARY"
	fi

	printf 'land.sh: %s\n' "$*"
}

fetch_branch() {
	git fetch --quiet "$Remote" "+refs/heads/$Branch:refs/remotes/$Remote/$Branch"
}

land_trailers() {
	git log -1 --format=%B "$1" | git interpret-trailers --parse \
		| awk -v key="$LandTrailer" "$AwkIsLandLine"' is_land($0) { sub(/^[^:]*:[[:space:]]*/, ""); print }'
}

remote_ref_target() {
	local listing
	listing="$(git ls-remote "$Remote" "$1")" || fail "$Remote does not answer for $1"
	printf '%s' "${listing%%[[:space:]]*}"
}

pinned_tag_at() {
	local tag
	tag="$(git show "$1:$SeedPinFile" | tr -d '[:space:]')"
	[ -n "$tag" ] || fail "$SeedPinFile is empty at $1"
	printf '%s' "$tag"
}

fetch_pinned_seed() {
	local tag
	tag="$(pinned_tag_at "$1")"
	./scripts/fetch-seed.sh "$tag" "--output=$2" > /dev/null
}

# Each tree is built in a worktree of its own, so a step never rewrites the script that runs it. A directory
# that is not that worktree would let `git -C` reach the enclosing checkout, so it is replaced.
prepare_tree() {
	local commit="$1" compiler="$2" bootstrap toplevel=""

	git worktree prune

	if [ -d "$ChainTree" ]; then
		toplevel="$(git -C "$ChainTree" rev-parse --show-toplevel 2> /dev/null || true)"

		if [ "$toplevel" != "$(cd "$ChainTree" && pwd -P)" ]; then
			rm -rf "$ChainTree"
			git worktree prune
		fi
	fi

	if [ ! -d "$ChainTree" ]; then
		git worktree add --quiet --detach "$ChainTree" "$commit"
	fi

	git -C "$ChainTree" checkout --quiet --force --detach "$commit"
	git -C "$ChainTree" clean -ffdxq
	bootstrap="$(maxon_downloaded_path "$ChainTree")"
	mkdir -p "$(dirname "$bootstrap")"
	cp "$compiler" "$bootstrap"
}

build_prepared_tree() {
	(cd "$ChainTree" && ./scripts/build-from-seed.sh) > "$1" 2>&1
}

walk_chain() {
	local base="$1" head="$2" guard="$3" compiler commit step=0

	rm -rf "$ChainCompilers"
	mkdir -p "$ChainCompilers"
	compiler="$ChainCompilers/seed"
	fetch_pinned_seed "$base" "$compiler"

	for commit in $(git rev-list --reverse "$base..$head"); do
		case "$guard" in
			"$WalkGuarded") require_base_unmoved ;;
			"$WalkUnguarded") ;;
			*) fail "walk_chain does not know the guard $guard" ;;
		esac

		step=$((step + 1))
		echo "land.sh: chain step $step builds $(git log -1 --format='%h %s' "$commit")" >&2

		prepare_tree "$commit" "$compiler"
		build_prepared_tree "$ChainCompilers/step-$step.log" \
			|| { tail -n "$VerdictTailLines" "$ChainCompilers/step-$step.log" >&2; fail "the compiler built by the step before it does not build $commit (chain step $step) — the log's tail is above"; }

		compiler="$ChainCompilers/step-$step"
		cp "$(maxon_compiler_path "$ChainTree")" "$compiler"
	done

	printf '%s\n' "$compiler"
}

# Only the message's final paragraph can hold trailers, and a message of one paragraph holds none.
landed_message() {
	local head="$1" chain="$2" commit trailers=()

	for commit in $chain; do
		trailers+=(--trailer "$ChainTrailer: $commit")
	done

	git log -1 --format=%B "$head" \
		| awk -v key="$LandTrailer" "$AwkIsLandLine"' { lines[NR] = $0; if ($0 != "") last = NR }
			END {
				for (i = 1; i <= last; i++) if (lines[i] == "") blank = i
				for (i = 1; i <= last; i++) if (blank == 0 || i <= blank || !is_land(lines[i])) print lines[i]
			}' \
		| git interpret-trailers --where end "${trailers[@]}"
}

commit_as_bot() {
	local author_name="$1" author_email="$2" author_date="$3"
	shift 3

	env GIT_AUTHOR_NAME="$author_name" GIT_AUTHOR_EMAIL="$author_email" GIT_AUTHOR_DATE="$author_date" \
		GIT_COMMITTER_NAME="$BotName" GIT_COMMITTER_EMAIL="$BotEmail" GIT_COMMITTER_DATE="$LAND_COMMITTED $UtcOffset" \
		git commit-tree "$@"
}

make_landed() {
	local head="$1" base="$2" chain="$3" name email date

	name="$(git log -1 --format=%an "$head")"
	email="$(git log -1 --format=%ae "$head")"
	date="$(git log -1 --format=%ad --date=raw "$head")"
	landed_message "$head" "$chain" | commit_as_bot "$name" "$email" "$date" "$head^{tree}" -p "$base"
}

make_pin() {
	local landed="$1" tag="$2" blob index tree

	blob="$(printf '%s\n' "$tag" | git hash-object -w --stdin)"
	index="$(git rev-parse --git-path land-pin-index)"
	rm -f "$index"
	GIT_INDEX_FILE="$index" git read-tree "$landed"
	GIT_INDEX_FILE="$index" git update-index --cacheinfo "100644,$blob,$SeedPinFile"
	tree="$(GIT_INDEX_FILE="$index" git write-tree)"
	rm -f "$index"

	printf 'The tree is built with %s, frozen from %s\n' "$tag" "${landed:0:$SeedCommitAbbreviation}" \
		| commit_as_bot "$BotName" "$BotEmail" "$LAND_COMMITTED $UtcOffset" "$tree" -p "$landed"
}

# `S` and the pin are recomputed in every job from the plan's outputs; a different sha is a different landing.
recreate_landed() {
	local landed

	require_env LAND_HEAD LAND_BASE LAND_CHAIN LAND_COMMITTED LAND_LANDED
	landed="$(make_landed "$LAND_HEAD" "$LAND_BASE" "$LAND_CHAIN")"
	[ "$landed" = "$LAND_LANDED" ] || fail "the landed commit came out as $landed, not the plan's $LAND_LANDED"
}

recreate_pin() {
	local pin

	require_env LAND_TAG LAND_PIN
	pin="$(make_pin "$LAND_LANDED" "$LAND_TAG")"
	[ "$pin" = "$LAND_PIN" ] || fail "the pin commit came out as $pin, not the plan's $LAND_PIN"
}

require_tag_free() {
	local name target

	if seed_release_published "$LAND_TAG"; then
		fail "the release $LAND_TAG already exists — a seed tag never moves"
	fi

	for name in "$LAND_TAG" "$LAND_TAG$ChainRefSuffix"; do
		target="$(remote_ref_target "refs/tags/$name")"
		[ -z "$target" ] || fail "$Remote already has the tag $name — a seed tag never moves"
	done
}

classify() {
	local head="$1" commits="$2" verdict="$3"

	case "$verdict/$commits" in
		"$VerdictAccepted/1") printf '%s' "$KindOrdinary" ;;
		"$VerdictAccepted/"*) fail "the pinned seed accepts $head, and an ordinary land is one commit, not $commits — squash the branch onto $Remote/$Branch" ;;
		"$VerdictRefused/1")
			tail -n "$VerdictTailLines" "$VerdictLog" >&2
			fail "the pinned seed refuses $head (its build's tail is above), and the branch carries no bootstrap chain — commit the intermediate tree the pinned seed accepts before it, so that tree's compiler builds $head"
			;;
		"$VerdictRefused/"*) printf '%s' "$KindChain" ;;
		*) fail "no classification for a $verdict head of $commits commits" ;;
	esac
}

landing_mode() {
	local head="$1" trailers

	trailers="$(land_trailers "$head")"

	case "$LAND_EVENT" in
		workflow_dispatch) printf '%s' "${LAND_MODE:?land.yml passes the dispatch input mode}" ;;
		push)
			[ -n "$trailers" ] || return 0
			[ "$(printf '%s\n' "$trailers" | wc -l)" -eq 1 ] || fail "$head carries more than one \`$LandTrailer:\` trailer"
			printf '%s' "$trailers"
			;;
		*) fail "land.yml does not run on $LAND_EVENT" ;;
	esac
}

command_plan() {
	local head mode count base merges pin_changes verdict kind native chain="" committed="" date="" landed="" tag="" pin=""

	require_env LAND_EVENT
	head="$(git rev-parse --verify "${1:?land.sh plan needs the head commit}^{commit}")"
	mode="$(landing_mode "$head")"

	if [ -z "$mode" ]; then
		summary "$head carries no \`$LandTrailer:\` trailer — nothing to land"
		output kind "$KindNone"
		return 0
	fi

	case "$mode" in
		"$ModeMain" | "$ModeRehearsal") ;;
		*) fail "\`$LandTrailer: $mode\` is not a landing mode — use \`$LandTrailer: $ModeMain\` or \`$LandTrailer: $ModeRehearsal\`" ;;
	esac

	fetch_branch
	base="$(git rev-parse "$Remote/$Branch")"
	git merge-base --is-ancestor "$base" "$head" || fail "$head does not fast-forward $Remote/$Branch — rebase onto it and push again"
	count="$(git rev-list --count "$base..$head")"
	[ "$count" -gt 0 ] || fail "$head is already on $Remote/$Branch — nothing to land"
	merges="$(git rev-list --merges "$base..$head")"
	[ -z "$merges" ] || fail "the branch holds a merge commit — a land is a linear run of commits on $Remote/$Branch"
	pin_changes="$(git rev-list "$base..$head" -- "$SeedPinFile")"
	[ -z "$pin_changes" ] || fail "a commit on the branch changes $SeedPinFile — the landing writes the pin"

	mkdir -p "$LandWorkDirectory"
	fetch_pinned_seed "$base" "$LandWorkDirectory/seed"
	prepare_tree "$head" "$LandWorkDirectory/seed"

	if build_prepared_tree "$VerdictLog"; then
		verdict="$VerdictAccepted"
	else
		verdict="$VerdictRefused"
	fi

	kind="$(classify "$head" "$count" "$verdict")"
	native="$(./scripts/targets/matrix.sh json native)"

	if [ "$kind" = "$KindChain" ]; then
		LAND_COMMITTED="$(date -u +%s)"
		committed="$LAND_COMMITTED"
		chain="$(git rev-list --reverse "$base..$head" | tr '\n' ' ')"
		chain="${chain% }"
		landed="$(make_landed "$head" "$base" "$chain")"
		date="$(date -u -d "@$committed" +"$SeedDateFormat")"
		tag="$(seed_tag "$date" "$landed")"
		LAND_TAG="$tag"
		require_tag_free
		pin="$(make_pin "$landed" "$tag")"
	fi

	output mode "$mode"
	output kind "$kind"
	output base "$base"
	output head "$head"
	output native "$native"
	output chain "$chain"
	output committed "$committed"
	output date "$date"
	output landed "$landed"
	output tag "$tag"
	output pin "$pin"

	summary "$mode: $head, $count commit(s) on ${base:0:$SeedCommitAbbreviation}, the pinned seed's verdict: $verdict — kind: $kind${tag:+ with the seed $tag}"
}

command_chain() {
	local base="${1:?land.sh chain needs the base commit}" head="${2:?land.sh chain needs the head commit}"

	base="$(git rev-parse --verify "$base^{commit}")"
	head="$(git rev-parse --verify "$head^{commit}")"
	git merge-base --is-ancestor "$base" "$head" || fail "$base is not an ancestor of $head"
	walk_chain "$base" "$head" "$WalkUnguarded"
}

append_chain_notes() {
	local commit seed subject

	seed="$(pinned_tag_at "$LAND_BASE")"

	{
		printf '\nBuilt by the bootstrap chain `%s%s`, each commit by the compiler of the one before it and the\n' "$LAND_TAG" "$ChainRefSuffix"
		printf 'first by the seed `%s` pinned at %s:\n\n' "$seed" "$LAND_BASE"

		for commit in $LAND_CHAIN; do
			subject="$(git log -1 --format=%s "$commit")"
			printf -- '- %s %s\n' "$commit" "$subject"
		done
	} >> "$SeedNotesFile"
}

command_freeze() {
	local compiler

	require_env LAND_DATE
	require_base_unmoved
	recreate_landed
	compiler="$(walk_chain "$LAND_BASE" "$LAND_HEAD" "$WalkGuarded")"
	git -C "$ChainTree" checkout --quiet --detach "$LAND_LANDED"
	cmp -s "$compiler" "$(maxon_compiler_path "$ChainTree")" || fail "the chain's last compiler is not the slot of $ChainTree"

	(cd "$ChainTree" && ./scripts/freeze-seed.sh "--date=$LAND_DATE")
	rm -rf "$SeedDistDirectory"
	mkdir -p "$(dirname "$SeedDistDirectory")"
	cp -R "$ChainTree/$SeedDistDirectory" "$SeedDistDirectory"
	seed_release_require_assets "$LAND_TAG"
	append_chain_notes
}

command_prove() {
	local lane="${1:?land.sh prove needs a lane}" host exe_ext

	require_env LAND_TAG
	require_base_unmoved
	maxon_require_long_paths land.sh || exit 1
	host="$(maxon_host_target)"
	[ "$lane" = "$host" ] || fail "a $lane seed is proved on a $lane runner, and this runner is $host"
	exe_ext="$(maxon_target_exe_ext "$lane")"

	./scripts/fetch-seed.sh "$LAND_TAG" "$lane" "--from=$SeedDistDirectory"
	"$(maxon_downloaded_path . "$exe_ext")" build maxon-bin "--output=$ProveOutput$exe_ext" \
		|| fail "the seed $LAND_TAG does not build this tree on $lane"
	summary "$lane: the seed $LAND_TAG builds the tree"
}

require_fetchable() {
	local target attempt

	for target in $MaxonTargets; do
		attempt=1

		until ./scripts/fetch-seed.sh "$LAND_TAG" "$target" "--output=$LandWorkDirectory/fetch-check/$target/maxon"; do
			[ "$attempt" -lt "$MaxFetchAttempts" ] || fail "$LAND_TAG is published but its $target seed does not fetch"
			attempt=$((attempt + 1))
			echo "land.sh: fetching $LAND_TAG for $target failed — retrying (attempt $attempt of $MaxFetchAttempts)" >&2
			sleep "$FetchRetrySeconds"
		done
	done
}

fail_base_moved() {
	fail "$Remote/$Branch moved past ${LAND_BASE:0:$SeedCommitAbbreviation} — nothing published; rebase and push again"
}

require_base_unmoved() {
	local tip

	require_env LAND_BASE
	tip="$(remote_ref_target "refs/heads/$Branch")"
	[ "$tip" = "$LAND_BASE" ] || fail_base_moved
}

command_publish() {
	recreate_landed
	recreate_pin
	seed_release_require_assets "$LAND_TAG"
	require_base_unmoved
	require_tag_free

	git push --quiet --atomic "$Remote" "$LAND_LANDED:refs/tags/$LAND_TAG" "$LAND_HEAD:refs/tags/$LAND_TAG$ChainRefSuffix" \
		|| fail "$Remote refused the tags $LAND_TAG and $LAND_TAG$ChainRefSuffix"
	seed_release_create "$LAND_TAG" || fail "creating the release $LAND_TAG failed"
	require_fetchable
	summary "published $LAND_TAG on ${LAND_LANDED:0:$SeedCommitAbbreviation}, its chain at $LAND_TAG$ChainRefSuffix"
}

# A re-run of this job after the retraction must not pin `main` to a seed that no longer exists.
require_still_published() {
	local target

	seed_release_published "$LAND_TAG" || fail "the release $LAND_TAG is gone — this run was retracted; push again"
	target="$(remote_ref_target "refs/tags/$LAND_TAG")"
	[ "$target" = "$LAND_LANDED" ] || fail "the tag $LAND_TAG is not on ${LAND_LANDED:0:$SeedCommitAbbreviation} — this run was retracted; push again"
}

command_land() {
	local tip

	require_env LAND_KIND LAND_HEAD
	require_base_unmoved

	case "$LAND_KIND" in
		"$KindOrdinary") tip="$LAND_HEAD" ;;
		"$KindChain")
			recreate_landed
			recreate_pin
			require_still_published
			tip="$LAND_PIN"
			;;
		*) fail "no land for a $LAND_KIND plan" ;;
	esac

	git push --quiet "$Remote" "$tip:refs/heads/$Branch" || fail_base_moved
	summary "$Branch is at ${tip:0:$SeedCommitAbbreviation}"
}

delete_remote_ref() {
	local ref="$1" expected="$2" target

	target="$(remote_ref_target "$ref")"

	if [ -z "$target" ]; then
		return 0
	fi

	if [ "$target" != "$expected" ]; then
		echo "land.sh: $ref names $target, not this run's $expected — left as it is" >&2
		return 0
	fi

	gh api --method DELETE "$SeedRepoApi/git/$ref" --silent || fail "deleting $ref failed — re-run this job"
	summary "deleted $ref"
}

command_retract() {
	local target

	require_env LAND_HEAD LAND_TAG LAND_LANDED LAND_PIN
	fetch_branch || fail "$Remote/$Branch cannot be read — nothing is retracted; re-run this job"

	if git merge-base --is-ancestor "$LAND_PIN" "$Remote/$Branch" 2> /dev/null; then
		summary "$Remote/$Branch holds the pin ${LAND_PIN:0:$SeedCommitAbbreviation} — $LAND_TAG stays published"
		return 0
	fi

	if seed_release_published "$LAND_TAG"; then
		target="$(remote_ref_target "refs/tags/$LAND_TAG")"
		[ "$target" = "$LAND_LANDED" ] || fail "the release $LAND_TAG is not on this run's ${LAND_LANDED:0:$SeedCommitAbbreviation} — left as it is"
		seed_release_delete || fail "deleting the release $LAND_TAG failed — re-run this job"
		summary "deleted the release $LAND_TAG"
	fi

	delete_remote_ref "refs/tags/$LAND_TAG" "$LAND_LANDED"
	delete_remote_ref "refs/tags/$LAND_TAG$ChainRefSuffix" "$LAND_HEAD"
}

# A run whose plan has not finished may yet be a chain land, so it counts until the plan says otherwise.
run_is_landing_chain() {
	local run="$1" jobs plan chain credential

	jobs="$(gh api "$SeedRepoApi/actions/runs/$run/jobs?per_page=100" --jq '.jobs[] | "\(.name)\t\(.status)\t\(.conclusion // "")"')" \
		|| fail "GitHub cannot say what the $LandWorkflow run $run is doing"
	plan="$(printf '%s\n' "$jobs" | awk -F '\t' -v name="$JobPlan" '$1 == name { print $2 "/" $3 }')"
	chain="$(printf '%s\n' "$jobs" | awk -F '\t' -v name="$JobChain" '$1 == name { print $3 }')"
	credential="$(printf '%s\n' "$jobs" | awk -F '\t' -v name="$JobCredential" '$1 == name { print $3 }')"

	case "$plan" in
		"$RunCompleted/$RunSuccess") [ "$chain" != "$JobSkipped" ] && [ "$credential" != "$JobSkipped" ] ;;
		"$RunCompleted/"*) return 1 ;;
		*) return 0 ;;
	esac
}

landing_chain_runs() {
	local status runs run branch url

	for status in $RunStatusesInFlight; do
		runs="$(gh api "$SeedRepoApi/actions/workflows/$LandWorkflow/runs?status=$status&per_page=100" \
			--jq '.workflow_runs[] | "\(.id)\t\(.head_branch)\t\(.html_url)"')" \
			|| fail "GitHub cannot list the $LandWorkflow runs — whether a chain land is in flight is unknown"

		while IFS=$'\t' read -r run branch url; do
			[ -n "$run" ] || continue

			if run_is_landing_chain "$run"; then
				printf '%s %s\n' "$branch" "$url"
			fi
		done < <(printf '%s\n' "$runs")
	done
}

command_in_flight() {
	local wait="no" argument landing

	for argument in "$@"; do
		case "$argument" in
			--wait) wait="yes" ;;
			*) fail "unknown argument to in-flight: $argument" ;;
		esac
	done

	while true; do
		landing="$(landing_chain_runs)"

		if [ -z "$landing" ]; then
			echo "$InFlightNone"
			return 0
		fi

		printf '%s\n' "$landing"
		[ "$wait" = "yes" ] || return 0
		echo "land.sh: waiting for the chain land to finish before $Branch is rebased onto" >&2
		sleep "$InFlightPollSeconds"
	done
}

# The branch name holds the head's sha, so the run dispatched on it is the newest run on that branch.
find_submitted_run() {
	local branch="$1" attempt run

	for ((attempt = 1; attempt <= SubmitFindAttempts; attempt++)); do
		run="$(gh api "$SeedRepoApi/actions/workflows/$LandWorkflow/runs?event=workflow_dispatch&branch=$branch" --jq '.workflow_runs[0].id // empty' || true)"

		if [ -n "$run" ]; then
			printf '%s' "$run"
			return 0
		fi

		sleep "$SubmitPollSeconds"
	done

	fail "no $LandWorkflow run started on $branch — it is still pushed; delete it with: git push $Remote :refs/heads/$branch"
}

await_run() {
	local run="$1" state failures=0

	while true; do
		if state="$(gh api "$SeedRepoApi/actions/runs/$run" --jq .status)"; then
			failures=0
			[ "$state" != "$RunCompleted" ] || return 0
		else
			failures=$((failures + 1))
			[ "$failures" -lt "$MaxSubmitPollFailures" ] || fail "the run $run cannot be read ($failures consecutive failures) — it may still land; follow it on GitHub"
		fi

		sleep "$SubmitPollSeconds"
	done
}

command_submit() {
	local mode="$ModeMain" argument head trailers branch run conclusion url

	for argument in "$@"; do
		case "$argument" in
			--rehearsal) mode="$ModeRehearsal" ;;
			*) fail "unknown argument to submit: $argument" ;;
		esac
	done

	head="$(git rev-parse HEAD)"
	trailers="$(land_trailers "$head")"
	[ -z "$trailers" ] || fail "HEAD carries a \`$LandTrailer:\` trailer, so pushing it starts the land by itself — push the branch instead"
	branch="$SubmitBranchPrefix${head:0:$SeedCommitAbbreviation}"

	git push --quiet "$Remote" "$head:refs/heads/$branch"
	gh api --method POST "$SeedRepoApi/actions/workflows/$LandWorkflow/dispatches" -f ref="$branch" -f "inputs[mode]=$mode" --silent
	run="$(find_submitted_run "$branch")"
	url="$(gh api "$SeedRepoApi/actions/runs/$run" --jq .html_url)"
	echo "land.sh: $LandWorkflow is landing $head ($mode): $url"

	await_run "$run"
	conclusion="$(gh api "$SeedRepoApi/actions/runs/$run" --jq .conclusion)"
	git push --quiet "$Remote" ":refs/heads/$branch" || echo "land.sh: deleting $branch failed — run: git push $Remote :refs/heads/$branch" >&2

	[ "$conclusion" = "$RunSuccess" ] || fail "$LandWorkflow ended $conclusion: $url"
	echo "land.sh: $LandWorkflow succeeded ($mode)"

	if [ "$mode" = "$ModeMain" ]; then
		echo "land.sh: $Remote/$Branch now holds the landing; to follow it: git fetch $Remote $Branch && git reset --keep $Remote/$Branch"
	fi
}

subcommand="${1:-}"
[ "$#" -eq 0 ] || shift

case "$subcommand" in
	submit) command_submit "$@" ;;
	chain) command_chain "$@" ;;
	plan) command_plan "$@" ;;
	freeze) command_freeze ;;
	prove) command_prove "$@" ;;
	publish) command_publish ;;
	land) command_land ;;
	retract) command_retract ;;
	in-flight) command_in_flight "$@" ;;
	*) fail "usage: land.sh <submit|in-flight|chain|plan|freeze|prove|publish|land|retract> (docs/RELEASING.md)" ;;
esac
