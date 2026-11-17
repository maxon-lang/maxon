#!/usr/bin/env bash
#
# Land a branch on `main`: the steps of `.github/workflows/land.yml`, one subcommand per job, and the local
# entry point that starts it. `docs/RELEASING.md` owns the procedure ("A commit the pinned seed refuses lands
# with its own seed").
#
# Usage:
#   scripts/land.sh submit [--rehearsal] [--name=<n>]
#                                          # push HEAD to land/<n> (default: the current branch), run land.yml on it,
#                                          # and wait for the verdict; prints the sha main received
#   scripts/land.sh await <branch> <head> [--after=<run id>]
#                                          # wait for the land.yml run of <head> on <branch>; prints main's new sha
#   scripts/land.sh chain <base> <head>    # build the bootstrap chain base..head; prints the head's compiler
#   scripts/land.sh plan <head>            # land.yml's jobs; they read LAND_* from the environment
#   scripts/land.sh freeze
#   scripts/land.sh prove <lane>
#   scripts/land.sh publish
#   scripts/land.sh pin
#   scripts/land.sh land
#   scripts/land.sh consume                # delete the run's branch if it still points at the run's head
#   scripts/land.sh retract

set -euo pipefail
shopt -s inherit_errexit 2> /dev/null || true

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/host-binaries.sh
. scripts/lib/seed.sh
. scripts/lib/scale-tables.sh

AnnotationsFd=3
eval "exec $AnnotationsFd>&1"

Remote="origin"
Branch="main"
LandWorkflow="land.yml"
LandTrailer="Land"
ScaleNoteTrailer="Scale-Note"
ChainTrailer="Bootstrap-Chain"
FrozenFromTrailer="Frozen-From"
ModeMain="main"
ModeRehearsal="rehearsal"
KindNone="none"
KindOrdinary="ordinary"
KindChain="chain"
VerdictAccepted="accepted"
VerdictRefused="refused"
ProofBuilt="built"
ProofSkipped="skipped"
ChainRefSuffix="-chain"
JobPlan="plan"
JobSkipped="skipped"
SubmitBranchPrefix="land/"
LandWorkDirectory="temp/land-branch"
ChainTree="$LandWorkDirectory/tree"
ChainCompilers="$LandWorkDirectory/compilers"
ProveOutput="$LandWorkDirectory/prove/maxon"
VerdictLog="$LandWorkDirectory/verdict.log"
SeedAtBase="$LandWorkDirectory/seed"
SeedAtTip="$LandWorkDirectory/tip-seed"
MergedBuildLog="$LandWorkDirectory/merged.log"
ChainSeed="$LandWorkDirectory/chain-seed"
PinBuildDirectory="$LandWorkDirectory/pin"
ScaleTestLog="$LandWorkDirectory/scale-test.log"
ScaleLogAtBase="$LandWorkDirectory/scale-log-base.md"
ScaleLogAtHead="$LandWorkDirectory/scale-log-head.md"
LandMergeDirectory="temp/land-merge"
ChangedByCommit="$LandMergeDirectory/changed-by-commit"
ChangedByTip="$LandMergeDirectory/changed-by-tip"
OverlapPaths="$LandMergeDirectory/overlap"
ChangedByLanding="$LandMergeDirectory/changed-by-landing"
MergedScaleLog="$LandMergeDirectory/scale-log-merged.md"
LandedScaleLog="$LandMergeDirectory/scale-log-landed.md"
MintedRowsPerTable=1
LandBundleDirectory="temp/land-bundle"
LandBundle="$LandBundleDirectory/landed.bundle"
WouldPushBundle="$LandBundleDirectory/would-push.bundle"
LandedRef="refs/land/landed"
WouldPushRef="refs/land/would-push"
TreeIndexName="land-tree-index"
RegularFileMode="100644"
MergeAttribute="merge"
UnionMerge="union"
VerdictTailLines=40
BotName="github-actions[bot]"
BotEmail="41898282+github-actions[bot]@users.noreply.github.com"
UtcOffset="+0000"
MaxFetchAttempts=3
FetchRetrySeconds=15
FirstLandAttempt=1
MaxLandAttempts=3
MaxPushAttempts=3
MomentPlan="plan"
MomentPush="push"
RunNamePrefix="land "
FullCommitPattern='^([0-9a-f]{40}|[0-9a-f]{64})$'
SubmitPollSeconds=30
SubmitFindAttempts=20
MaxSubmitPollFailures=10
RunCompleted="completed"
RunSuccess="success"
AnnotationNotice="notice"
AnnotationError="error"
AnnotationFailureLevel="failure"
FailureTitle="land.sh"
LandedTitle="landed"
RehearsedTitle="rehearsed"
LandedVerb="received"
RehearsedVerb="would receive"
# The measured compiles inherit this environment and its size enters the Bytes rows: a variable
# added here, or passed through from the runner, shifts every row.
ScaleTestEnvironment=(
	PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
	HOME=/home/runner
	TMPDIR=/tmp
)
RedispatchedTitle="redispatched"
OutputRedispatched="redispatched"
OutputReused="reused"
OutputYes="true"

# Every input the pinned seed's build reads, as `ci.yml` counts them: one missing here lets a merge
# that changes it skip the seed proof.
SeedInputs=(
	maxon-bin
	stdlib
	runtime
	vendor
	project.maxon
	maxon.maxtasks
	scripts/seed-tag
	scripts/build-from-seed.sh
	scripts/fetch-seed.sh
	scripts/fetch-vendor.sh
	scripts/lib
)

# The trailer key is matched case-insensitively, as git matches trailer keys.
AwkIsKeyLine='function is_key(line,    rest) { if (index(tolower(line), tolower(key)) != 1) return 0; rest = substr(line, length(key) + 1); return rest ~ /^[ \t]*:/ }'

annotate() {
	local level="$1" title="$2" message="$3" percent="%" carriage=$'\r' newline=$'\n'

	message="${message//"$percent"/%25}"
	message="${message//"$carriage"/%0D}"
	message="${message//"$newline"/%0A}"
	printf '::%s title=%s::%s\n' "$level" "$title" "$message" >&"$AnnotationsFd"
}

fail() {
	if [ -n "${GITHUB_ACTIONS:-}" ]; then
		annotate "$AnnotationError" "$FailureTitle" "$*"
	fi

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

short() {
	printf '%s' "${1:0:$SeedCommitAbbreviation}"
}

fetch_branch() {
	git fetch --quiet "$Remote" "+refs/heads/$Branch:refs/remotes/$Remote/$Branch"
}

main_tip() {
	fetch_branch || fail "$Remote/$Branch cannot be fetched"
	git rev-parse --verify "$Remote/$Branch^{commit}"
}

resolve_commit() {
	local ref="$1" refusal="${2:-tip and moved name a branch or a full commit id}"

	git fetch --quiet "$Remote" "$ref" || fail "$Remote has no $ref — $refusal"
	git rev-parse --verify "FETCH_HEAD^{commit}"
}

trailer_values() {
	awk -v key="$1" "$AwkIsKeyLine"' is_key($0) { sub(/^[^:]*:[ \t]*/, ""); print }'
}

line_count() {
	if [ -z "$1" ]; then
		printf '0'
	else
		printf '%s\n' "$1" | wc -l | tr -d '[:space:]'
	fi
}

commit_trailer() {
	local commit="$1" key="$2" message parsed written parsed_count written_count

	message="$(git log -1 --format=%B "$commit")"
	parsed="$(printf '%s\n' "$message" | git interpret-trailers --parse | trailer_values "$key")"
	written="$(printf '%s\n' "$message" | trailer_values "$key")"
	parsed_count="$(line_count "$parsed")"
	written_count="$(line_count "$written")"

	[ "$written_count" -le "$parsed_count" ] || fail "the \`$key:\` line of $(short "$commit") is not in the commit's final paragraph — move it into the final paragraph"
	[ "$parsed_count" -le 1 ] || fail "$(short "$commit") carries more than one \`$key:\` trailer"
	printf '%s' "$parsed"
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
	local base="$1" head="$2" compiler commit step=0

	rm -rf "$ChainCompilers"
	mkdir -p "$ChainCompilers"
	compiler="$ChainCompilers/seed"
	fetch_pinned_seed "$base" "$compiler"

	for commit in $(git rev-list --reverse "$base..$head"); do
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

# `commit_trailer` runs first: the strip drops every `Land:` line, which is right only once git has
# parsed each one as a trailer.
landed_message() {
	local head="$1" added="$2" trailer trailers=()

	while IFS= read -r trailer; do
		[ -z "$trailer" ] || trailers+=(--trailer "$trailer")
	done <<< "$added"

	commit_trailer "$head" "$LandTrailer" > /dev/null

	git log -1 --format=%B "$head" \
		| awk -v key="$LandTrailer" "$AwkIsKeyLine"'
			{ lines[NR] = $0 }
			END {
				for (i = 1; i <= NR; i++) {
					if (dropping && lines[i] ~ /^[ \t]+[^ \t\r]/) {
						continue
					}

					dropping = is_key(lines[i])

					if (!dropping) {
						kept[++count] = lines[i]

						if (lines[i] !~ /^[ \t\r]*$/) {
							last = count
						}
					}
				}

				for (i = 1; i <= last; i++) print kept[i]
			}' \
		| git interpret-trailers --where end "${trailers[@]}"
}

commit_as_bot() {
	local author_name="$1" author_email="$2" author_date="$3"
	shift 3

	require_env LAND_COMMITTED
	env GIT_AUTHOR_NAME="$author_name" GIT_AUTHOR_EMAIL="$author_email" GIT_AUTHOR_DATE="$author_date" \
		GIT_COMMITTER_NAME="$BotName" GIT_COMMITTER_EMAIL="$BotEmail" GIT_COMMITTER_DATE="$LAND_COMMITTED $UtcOffset" \
		git commit-tree "$@"
}

commit_from() {
	local source="$1" trailers="$2" tree="$3" parent="$4" name email date

	name="$(git log -1 --format=%an "$source")"
	email="$(git log -1 --format=%ae "$source")"
	date="$(git log -1 --format=%ad --date=raw "$source")"
	landed_message "$source" "$trailers" | commit_as_bot "$name" "$email" "$date" "$tree" -p "$parent"
}

chain_trailers() {
	local commit

	for commit in $1; do
		printf '%s: %s\n' "$ChainTrailer" "$commit"
	done
}

chain_landed() {
	local head="$1" chain="$2" base="$3" trailers

	trailers="$(chain_trailers "$chain")"
	commit_from "$head" "$trailers" "$head^{tree}" "$base"
}

tree_with_blob() {
	local commit="$1" path="$2" blob="$3" index tree

	index="$(git rev-parse --git-path "$TreeIndexName")"
	rm -f "$index"
	GIT_INDEX_FILE="$index" git read-tree "$commit"
	GIT_INDEX_FILE="$index" git update-index --cacheinfo "$RegularFileMode,$blob,$path"
	tree="$(GIT_INDEX_FILE="$index" git write-tree)"
	rm -f "$index"
	printf '%s' "$tree"
}

make_pin() {
	local merged="$1" frozen="$2" tag="$3" blob tree

	blob="$(printf '%s\n' "$tag" | git hash-object -w --stdin)"
	tree="$(tree_with_blob "$merged" "$SeedPinFile" "$blob")"

	printf 'The tree is built with %s, frozen from %s\n' "$tag" "$(short "$frozen")" \
		| commit_as_bot "$BotName" "$BotEmail" "$LAND_COMMITTED $UtcOffset" "$tree" -p "$merged"
}

# `S` is recomputed in every job from the plan's outputs; a different sha is a different landing.
recreate_landed() {
	local landed

	require_env LAND_HEAD LAND_BASE LAND_CHAIN LAND_COMMITTED LAND_LANDED
	landed="$(chain_landed "$LAND_HEAD" "$LAND_CHAIN" "$LAND_BASE")"
	[ "$landed" = "$LAND_LANDED" ] || fail "the landed commit came out as $landed, not the plan's $LAND_LANDED"
}

chain_merged_onto() {
	local tip="$1" base="$2" landed="$3"

	if [ "$tip" = "$base" ]; then
		printf '%s' "$landed"
	else
		merge_onto "$tip" "$base" "$landed" "$FrozenFromTrailer: $landed"
	fi
}

require_tag_free() {
	local name target

	if seed_release_published "$LAND_TAG"; then
		fail "the release $LAND_TAG already exists — a seed tag never moves"
	fi

	for name in "$LAND_TAG" "$LAND_TAG$ChainRefSuffix"; do
		target="$(remote_ref_target "refs/tags/$name")" || exit 1
		[ -z "$target" ] || fail "$Remote already has the tag $name — a seed tag never moves"
	done
}

seed_inputs_differ() {
	local status=0

	git diff --quiet "$1" "$2" -- "${SeedInputs[@]}" || status=$?

	case "$status" in
		0) return 1 ;;
		1) return 0 ;;
		*) fail "comparing the seed inputs of $(short "$1") and $(short "$2") failed (git diff exited $status)" ;;
	esac
}

is_union_merged() {
	local tip="$1" path="$2" attribute

	attribute="$(git check-attr --source="$tip" "$MergeAttribute" -- "$path")" || fail "git cannot read the $MergeAttribute attribute of $path at $(short "$tip")"
	[ "${attribute##*: }" = "$UnionMerge" ]
}

changed_paths() {
	local from="$1" to="$2" list="$3"

	git diff -z --no-renames --name-only "$from" "$to" > "$list" || fail "git cannot list the paths $(short "$to") changes since $(short "$from")"
}

overlapping_paths() {
	local tip="$1" base="$2" commit="$3" path by_commit by_tip
	local -A changed_by_tip=()

	changed_paths "$base" "$commit" "$ChangedByCommit"
	changed_paths "$base" "$tip" "$ChangedByTip"
	mapfile -d '' -t by_commit < "$ChangedByCommit"
	mapfile -d '' -t by_tip < "$ChangedByTip"

	for path in "${by_tip[@]}"; do
		changed_by_tip["$path"]=1
	done

	for path in "${by_commit[@]}"; do
		if [ -n "${changed_by_tip["$path"]:-}" ] && ! is_union_merged "$tip" "$path"; then
			printf '%s\0' "$path"
		fi
	done
}

joined_paths() {
	local path list=""

	for path in "$@"; do
		list="${list:+$list, }\`$path\`"
	done

	printf '%s' "$list"
}

merged_tree() {
	local tip="$1" base="$2" commit="$3" merged

	merged="$(git --attr-source="$tip" merge-tree --write-tree --merge-base="$base" "$tip" "$commit")" || return 1
	printf '%s' "${merged%%$'\n'*}"
}

merge_onto() {
	local tip="$1" base="$2" commit="$3" trailers="${4:-}" overlap tree

	git merge-base --is-ancestor "$base" "$tip" || fail "$(short "$base") is not an ancestor of $(short "$tip") — $Remote/$Branch was rewritten; submit again"
	git merge-base --is-ancestor "$base" "$commit" || fail "$(short "$base") is not an ancestor of $(short "$commit")"
	mkdir -p "$LandMergeDirectory"
	overlapping_paths "$tip" "$base" "$commit" > "$OverlapPaths"
	mapfile -d '' -t overlap < "$OverlapPaths"
	[ "${#overlap[@]}" -eq 0 ] || fail "$(short "$commit") and $(short "$tip") both change $(joined_paths "${overlap[@]}") since $(short "$base") — rebase by hand and submit again"

	tree="$(merged_tree "$tip" "$base" "$commit")" \
		|| fail "$(short "$commit") does not merge cleanly onto $(short "$tip") — rebase by hand and submit again"
	commit_from "$commit" "$trailers" "$tree" "$tip"
}

scale_log_or_empty() {
	scale_log_at "$1" "$2" || [ "$?" -eq "$ScaleLogAbsent" ] || fail "git cannot read $ScaleLogPath at $(short "$1")"
}

require_no_hand_row() {
	local base="$1" head="$2" base_rows head_rows

	if git diff --quiet "$base" "$head" -- "$ScaleLogPath"; then
		return 0
	fi

	mkdir -p "$LandWorkDirectory"
	scale_log_or_empty "$base" "$ScaleLogAtBase"
	scale_log_or_empty "$head" "$ScaleLogAtHead"
	base_rows="$(scale_table_rows "$ScaleLogAtBase")"
	head_rows="$(scale_table_rows "$ScaleLogAtHead")"
	[ "$base_rows" = "$head_rows" ] || fail "$(short "$head") changes a row of the scale-history tables in $ScaleLogPath — $LandWorkflow mints the row: drop the edit and carry the note as a \`$ScaleNoteTrailer:\` trailer in the commit's final paragraph"
}

mint_row() {
	local merged="$1" tip="$2" note="$3" compiler blob tree

	compiler="$repo_root/$(maxon_compiler_path "$ChainTree")"
	(cd "$ChainTree" && env -i "${ScaleTestEnvironment[@]}" "$compiler" scale-test "--note=$note") > "$ScaleTestLog" 2>&1 \
		|| { tail -n "$VerdictTailLines" "$ScaleTestLog" >&2; fail "scale-test recorded no row for the \`$ScaleNoteTrailer:\` of $(short "$merged") (its tail is above) — fix what it names and submit again"; }
	blob="$(git -C "$ChainTree" hash-object -w "$ScaleLogPath")"
	tree="$(tree_with_blob "$merged" "$ScaleLogPath" "$blob")"
	commit_from "$merged" "" "$tree" "$tip"
}

bundle_commit() {
	local commit="$1" prerequisite="$2" ref="$3" bundle="$4"

	git update-ref "$ref" "$commit"
	mkdir -p "$(dirname "$bundle")"
	git bundle create --quiet "$bundle" "$ref" "^$prerequisite"
}

receive_landed() {
	local received

	git bundle verify --quiet "$LandBundle" || fail "the plan's bundle $LandBundle does not verify against this clone"
	git fetch --quiet "$LandBundle" "+$LandedRef:$LandedRef"
	received="$(git rev-parse --verify "$LandedRef^{commit}")"
	[ "$received" = "$LAND_LANDED" ] || fail "the plan's bundle holds $received, not the plan's $LAND_LANDED"
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
	local land="$1"

	case "$LAND_EVENT" in
		workflow_dispatch) printf '%s' "${LAND_MODE:?land.yml passes the dispatch input mode}" ;;
		push) printf '%s' "$land" ;;
		*) fail "land.yml does not run on $LAND_EVENT" ;;
	esac
}

require_attempt() {
	local refusal

	require_env LAND_ATTEMPT
	refusal="the dispatch input attempt is $LAND_ATTEMPT, not a number from $FirstLandAttempt to $MaxLandAttempts"

	case "$LAND_ATTEMPT" in
		*[!0-9]*) fail "$refusal" ;;
		*) ;;
	esac

	LAND_ATTEMPT="$((10#$LAND_ATTEMPT))"
	[ "$LAND_ATTEMPT" -ge "$FirstLandAttempt" ] && [ "$LAND_ATTEMPT" -le "$MaxLandAttempts" ] || fail "$refusal"
}

require_dispatched_head() {
	local head="$1"

	if [ -z "${LAND_HEAD_INPUT:-}" ]; then
		return 0
	fi

	require_env LAND_BRANCH
	[[ "$LAND_HEAD_INPUT" =~ $FullCommitPattern ]] || fail "the dispatch input head is $LAND_HEAD_INPUT, not a full commit id"
	[ "$LAND_HEAD_INPUT" = "$head" ] || fail "this run was dispatched to land $LAND_HEAD_INPUT, and $LAND_BRANCH has moved to $head since — $LAND_BRANCH was pushed again, so this land stops; submit again"
}

require_no_stand_ins() {
	[ -z "${LAND_TIP_INPUT:-}${LAND_MOVED_INPUT:-}" ] || fail "the dispatch inputs tip and moved rehearse a land, and $1 — drop them"
}

standing_tip() {
	local mode="$1" moment="$2"

	case "$mode/$moment" in
		"$ModeMain/$MomentPlan" | "$ModeMain/$MomentPush")
			require_no_stand_ins "$ModeMain mode refuses them"
			main_tip
			;;
		"$ModeRehearsal/$MomentPlan")
			if [ -n "${LAND_MOVED_INPUT:-}" ]; then
				resolve_commit "$LAND_MOVED_INPUT" > /dev/null
			fi

			if [ -n "${LAND_TIP_INPUT:-}" ]; then
				resolve_commit "$LAND_TIP_INPUT"
			else
				main_tip
			fi
			;;
		"$ModeRehearsal/$MomentPush")
			if [ -n "${LAND_MOVED_INPUT:-}" ]; then
				resolve_commit "$LAND_MOVED_INPUT"
			elif [ -n "${LAND_TIP_INPUT:-}" ]; then
				require_env LAND_TIP
				printf '%s' "$LAND_TIP"
			else
				main_tip
			fi
			;;
		"$ModeMain/"* | "$ModeRehearsal/"*) fail "standing_tip does not know the moment $moment" ;;
		*) fail "\`$LandTrailer: $mode\` is not a landing mode — use \`$LandTrailer: $ModeMain\` or \`$LandTrailer: $ModeRehearsal\`" ;;
	esac
}

chain_commits() {
	local base="$1" head="$2" chain

	chain="$(git rev-list --reverse "$base..$head" | tr '\n' ' ')"
	printf '%s' "${chain% }"
}

committed_date() {
	date -u -d "@$LAND_COMMITTED" +"$SeedDateFormat"
}

require_seed_of_head() {
	local seed="$1" head="$2" frozen="$3" base="$4" remedy="$5" chain_ref chain landed date tag

	chain_ref="$(remote_ref_target "refs/tags/$seed$ChainRefSuffix")" || exit 1
	[ "$chain_ref" = "$head" ] || fail "the chain ref $seed$ChainRefSuffix is not on $(short "$head") — $seed is not its seed; $remedy"
	LAND_COMMITTED="$(git log -1 --format=%ct "$frozen")"
	chain="$(chain_commits "$base" "$head")"
	landed="$(chain_landed "$head" "$chain" "$base")"
	[ "$landed" = "$frozen" ] || fail "the seed $seed was frozen from $(short "$frozen"), and $(short "$head") on $(short "$base") lands as $(short "$landed") at that commit's time — $seed is not its seed; $remedy"
	date="$(committed_date)"
	tag="$(seed_tag "$date" "$landed")"
	[ "$tag" = "$seed" ] || fail "the seed $seed was frozen from $(short "$frozen"), whose commit time names it $tag — $LandWorkflow did not publish it; $remedy"
}

command_plan() {
	local head land note mode tip base count merges pin_changes verdict kind classified native merged proof="" chain="" date="" landed="" frozen="" tag=""
	local seed="${LAND_SEED_INPUT:-}" reused=""

	require_env LAND_EVENT
	head="$(git rev-parse --verify "${1:?land.sh plan needs the head commit}^{commit}")"
	require_dispatched_head "$head"
	land="$(commit_trailer "$head" "$LandTrailer")"
	note="$(commit_trailer "$head" "$ScaleNoteTrailer")"
	mode="$(landing_mode "$land")"

	if [ -z "$mode" ]; then
		summary "$head carries no \`$LandTrailer:\` trailer — nothing to land"
		output kind "$KindNone"
		return 0
	fi

	require_attempt
	tip="$(standing_tip "$mode" "$MomentPlan")"
	base="$(git merge-base "$tip" "$head")" || fail "$(short "$head") shares no history with $(short "$tip")"
	count="$(git rev-list --count "$base..$head")"
	[ "$count" -gt 0 ] || fail "$head is already on $(short "$tip") — nothing to land"
	merges="$(git rev-list --merges "$base..$head")"
	[ -z "$merges" ] || fail "the branch holds a merge commit — a land is a linear run of commits on $Remote/$Branch"
	pin_changes="$(git rev-list "$base..$head" -- "$SeedPinFile")"
	[ -z "$pin_changes" ] || fail "a commit on the branch changes $SeedPinFile — the landing writes the pin"
	require_no_hand_row "$base" "$head"

	mkdir -p "$LandWorkDirectory"

	if [ -n "$seed" ]; then
		[ "$count" -gt 1 ] || fail "the dispatch input seed reuses a bootstrap chain's seed, and $head is one commit on $(short "$base") — an ordinary land takes no seed; submit again"
		kind="$KindChain"
		classified="reusing the seed $seed"
	else
		fetch_pinned_seed "$base" "$SeedAtBase"
		prepare_tree "$head" "$SeedAtBase"

		if build_prepared_tree "$VerdictLog"; then
			verdict="$VerdictAccepted"
		else
			verdict="$VerdictRefused"
		fi

		kind="$(classify "$head" "$count" "$verdict")"
		classified="the pinned seed's verdict: $verdict"
	fi

	native="$(./scripts/targets/matrix.sh json native)"
	LAND_COMMITTED="$(date -u +%s)"

	case "$kind" in
		"$KindChain")
			[ -z "$note" ] || fail "a bootstrap chain mints no scale row — drop the \`$ScaleNoteTrailer:\` trailer and carry it on an ordinary land"
			chain="$(chain_commits "$base" "$head")"

			if [ -n "$seed" ]; then
				frozen="$(resolve_commit "refs/tags/$seed" "the dispatch input seed names the seed a bootstrap chain's first attempt published")"
				require_seed_of_head "$seed" "$head" "$frozen" "$base" "submit again"
				seed_published_on "$seed" "$frozen" || fail "the release $seed is gone or its tag left $(short "$frozen") — it was retracted; submit again"
				reused="$OutputYes"
			fi

			landed="$(chain_landed "$head" "$chain" "$base")"
			date="$(committed_date)"
			tag="$(seed_tag "$date" "$landed")"
			LAND_TAG="$tag"

			if [ -z "$seed" ]; then
				require_tag_free
			fi

			chain_merged_onto "$tip" "$base" "$landed" > /dev/null
			;;
		"$KindOrdinary")
			merged="$(merge_onto "$tip" "$base" "$head")"

			if seed_inputs_differ "$head" "$merged"; then
				fetch_pinned_seed "$tip" "$SeedAtTip"
				prepare_tree "$merged" "$SeedAtTip"
				build_prepared_tree "$MergedBuildLog" \
					|| { tail -n "$VerdictTailLines" "$MergedBuildLog" >&2; fail "the seed pinned at $(short "$tip") does not build $(short "$head") merged onto it (the build's tail is above) — rebase by hand and submit again"; }
				proof="$ProofBuilt"
			else
				git -C "$ChainTree" checkout --quiet --detach "$merged"
				proof="$ProofSkipped"
			fi

			landed="$merged"

			if [ -n "$note" ]; then
				landed="$(mint_row "$merged" "$tip" "$note")"
			fi

			bundle_commit "$landed" "$tip" "$LandedRef" "$LandBundle"
			;;
		*) fail "no plan for a $kind land" ;;
	esac

	output mode "$mode"
	output kind "$kind"
	output base "$base"
	output tip "$tip"
	output head "$head"
	output native "$native"
	output chain "$chain"
	output committed "$LAND_COMMITTED"
	output date "$date"
	output landed "$landed"
	output tag "$tag"
	output "$OutputReused" "$reused"

	summary "$mode: $head, $count commit(s) on $(short "$base"), merged onto $(short "$tip"), $classified — kind: $kind${proof:+, seed proof of the merged tree: $proof}${note:+, a scale row minted}${tag:+ with the seed $tag}"
}

command_chain() {
	local base="${1:?land.sh chain needs the base commit}" head="${2:?land.sh chain needs the head commit}"

	base="$(git rev-parse --verify "$base^{commit}")"
	head="$(git rev-parse --verify "$head^{commit}")"
	git merge-base --is-ancestor "$base" "$head" || fail "$base is not an ancestor of $head"
	walk_chain "$base" "$head"
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
	recreate_landed
	compiler="$(walk_chain "$LAND_BASE" "$LAND_HEAD")"
	git -C "$ChainTree" checkout --quiet --detach "$LAND_LANDED"
	cmp -s "$compiler" "$(maxon_compiler_path "$ChainTree")" || fail "the chain's last compiler is not the slot of $ChainTree"

	(cd "$ChainTree" && ./scripts/freeze-seed.sh "--date=$LAND_DATE")
	rm -rf "$SeedDistDirectory"
	mkdir -p "$(dirname "$SeedDistDirectory")"
	cp -R "$ChainTree/$SeedDistDirectory" "$SeedDistDirectory"
	seed_release_require_assets "$LAND_TAG"
	append_chain_notes
}

seed_builds_compiler() {
	local tree="$1" target="$2" output="$3"

	(cd "$tree" && "$(maxon_downloaded_path .)" build maxon-bin "--target=$target" "--output=$output")
}

command_prove() {
	local lane="${1:?land.sh prove needs a lane}" host exe_ext

	require_env LAND_TAG
	maxon_require_long_paths land.sh || exit 1
	host="$(maxon_host_target)"
	[ "$lane" = "$host" ] || fail "a $lane seed is proved on a $lane runner, and this runner is $host"
	exe_ext="$(maxon_target_exe_ext "$lane")"

	./scripts/fetch-seed.sh "$LAND_TAG" "$lane" "--from=$SeedDistDirectory"
	seed_builds_compiler . "$lane" "$ProveOutput$exe_ext" \
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

command_publish() {
	recreate_landed
	seed_release_require_assets "$LAND_TAG"
	require_tag_free

	git push --quiet --atomic "$Remote" "$LAND_LANDED:refs/tags/$LAND_TAG" "$LAND_HEAD:refs/tags/$LAND_TAG$ChainRefSuffix" \
		|| fail "$Remote refused the tags $LAND_TAG and $LAND_TAG$ChainRefSuffix"
	seed_release_create "$LAND_TAG" || fail "creating the release $LAND_TAG failed"
	require_fetchable
	summary "published $LAND_TAG on $(short "$LAND_LANDED"), its chain at $LAND_TAG$ChainRefSuffix"
}

cross_build_merged() {
	local merged="$1" tip="$2" seed target exe_ext log

	seed="$ChainSeed$MAXON_EXE_EXT"
	./scripts/fetch-seed.sh "$LAND_TAG" "$(maxon_host_target)" "--output=$seed"
	prepare_tree "$merged" "$seed"
	rm -rf "$PinBuildDirectory"

	for target in $MaxonTargets; do
		exe_ext="$(maxon_target_exe_ext "$target")"
		log="$PinBuildDirectory/$target.log"
		mkdir -p "$PinBuildDirectory/$target"
		echo "land.sh: the seed $LAND_TAG builds $(short "$merged") for $target" >&2
		seed_builds_compiler "$ChainTree" "$target" "$repo_root/$PinBuildDirectory/$target/maxon$exe_ext" > "$log" 2>&1 \
			|| { tail -n "$VerdictTailLines" "$log" >&2; fail "the seed $LAND_TAG does not build $(short "$merged"), the chain's $(short "$LAND_LANDED") merged onto $(short "$tip"), for $target (the build's tail is above) — rebase the chain onto $Remote/$Branch and submit again"; }
	done
}

command_pin() {
	local tip merged pin proof

	require_env LAND_MODE LAND_TAG
	recreate_landed
	tip="$(standing_tip "$LAND_MODE" "$MomentPlan")"
	merged="$(chain_merged_onto "$tip" "$LAND_BASE" "$LAND_LANDED")"

	if seed_inputs_differ "$LAND_LANDED" "$merged"; then
		cross_build_merged "$merged" "$tip"
		proof="$ProofBuilt"
	else
		proof="$ProofSkipped"
	fi

	pin="$(make_pin "$merged" "$LAND_LANDED" "$LAND_TAG")"

	output tip "$tip"
	output merged "$merged"
	output pin "$pin"

	summary "the chain's $(short "$LAND_LANDED") merged onto $(short "$tip") as $(short "$merged"), pinned to $LAND_TAG by $(short "$pin") — the seed's build of the merged tree: $proof"
}

seed_published_on() {
	local tag="$1" landed="$2" target

	seed_release_published "$tag" || return 1
	target="$(remote_ref_target "refs/tags/$tag")" || exit 1
	[ -n "$target" ] && [ "$target" = "$landed" ]
}

# A re-run of this job after the retraction must not pin `main` to a seed that no longer exists.
require_still_published() {
	seed_published_on "$LAND_TAG" "$LAND_LANDED" || fail "the release $LAND_TAG is gone or its tag is not on $(short "$LAND_LANDED") — this run was retracted; push again"
}

landing_words() {
	case "$1" in
		"$ModeMain") printf '%s\t%s\n' "$LandedTitle" "$LandedVerb" ;;
		"$ModeRehearsal") printf '%s\t%s\n' "$RehearsedTitle" "$RehearsedVerb" ;;
		*) fail "no landing annotation in $1 mode" ;;
	esac
}

record_landed() {
	local mode="$1" commit="$2" words title verb

	words="$(landing_words "$mode")"
	IFS=$'\t' read -r title verb <<< "$words"
	annotate "$AnnotationNotice" "$title" "$commit"
	summary "$Branch $verb $commit"
}

dispatch_land() {
	local branch="$1" mode="$2" head="$3" attempt="$4" tip="${5:-}" seed="${6:-}" fields

	fields=(-f "ref=$branch" -f "inputs[mode]=$mode" -f "inputs[head]=$head" -f "inputs[attempt]=$attempt")

	if [ -n "$tip" ]; then
		fields+=(-f "inputs[tip]=$tip")
	fi

	if [ -n "$seed" ]; then
		fields+=(-f "inputs[seed]=$seed")
	fi

	gh api --method POST "$SeedRepoApi/actions/workflows/$LandWorkflow/dispatches" "${fields[@]}" --silent \
		|| fail "dispatching $LandWorkflow on $branch for $(short "$head") failed — submit again"
}

redispatch() {
	local tip="$1" next seed

	[ "$LAND_ATTEMPT" -lt "$MaxLandAttempts" ] || fail "$Remote/$Branch moved to $(short "$tip"), which changes a seed input, on each of $MaxLandAttempts attempts — submit again"
	next=$((LAND_ATTEMPT + 1))

	case "$LAND_KIND" in
		"$KindOrdinary") seed="" ;;
		"$KindChain") seed="$LAND_TAG" ;;
		*) fail "no re-dispatch for a $LAND_KIND land" ;;
	esac

	case "$LAND_MODE" in
		"$ModeRehearsal") dispatch_land "$LAND_BRANCH" "$LAND_MODE" "$LAND_HEAD" "$next" "$tip" "$seed" ;;
		"$ModeMain") dispatch_land "$LAND_BRANCH" "$LAND_MODE" "$LAND_HEAD" "$next" "" "$seed" ;;
		*) fail "no re-dispatch in $LAND_MODE mode" ;;
	esac

	output "$OutputRedispatched" "$OutputYes"
	annotate "$AnnotationNotice" "$RedispatchedTitle" "attempt $next merges onto $tip"
	summary "$Remote/$Branch moved to $(short "$tip"), which changes a seed input — re-dispatched as attempt $next of $MaxLandAttempts"
}

require_minted_rows_only() {
	local merged="$1" appended

	scale_log_or_empty "$merged" "$MergedScaleLog"
	scale_log_or_empty "$LAND_LANDED" "$LandedScaleLog"
	appended="$(scale_rows_appended "$MergedScaleLog" "$LandedScaleLog")" \
		|| fail "the plan's landed commit changes $ScaleLogPath other than by appending rows to the scale-history tables — nothing is pushed"
	printf '%s\n' "$appended" | awk -F '\t' -v rows="$MintedRowsPerTable" '$2 != rows { wrong = 1 } END { exit wrong }' \
		|| fail "the plan's landed commit appends other than $MintedRowsPerTable row to each scale-history table of $ScaleLogPath — nothing is pushed"
}

require_landed_as_planned() {
	local merged rebuilt note changed

	require_env LAND_BASE
	merged="$(merge_onto "$LAND_TIP" "$LAND_BASE" "$LAND_HEAD")"
	note="$(commit_trailer "$LAND_HEAD" "$ScaleNoteTrailer")"

	if [ -z "$note" ]; then
		[ "$LAND_LANDED" = "$merged" ] || fail "the plan's landed commit $(short "$LAND_LANDED") is not $(short "$LAND_HEAD") merged onto $(short "$LAND_TIP"), which is $(short "$merged") — nothing is pushed"
		return 0
	fi

	rebuilt="$(commit_from "$merged" "" "$LAND_LANDED^{tree}" "$LAND_TIP")"
	[ "$rebuilt" = "$LAND_LANDED" ] || fail "the plan's landed commit $(short "$LAND_LANDED") is not one commit on $(short "$LAND_TIP") with the message, author and dates of $(short "$LAND_HEAD") merged onto it — nothing is pushed"

	changed_paths "$merged" "$LAND_LANDED" "$ChangedByLanding"
	mapfile -d '' -t changed < "$ChangedByLanding"
	[ "${#changed[@]}" -ne 0 ] || fail "the plan's landed commit holds no scale row for the \`$ScaleNoteTrailer:\` of $(short "$LAND_HEAD") — nothing is pushed"
	[ "${#changed[@]}" -eq 1 ] && [ "${changed[0]}" = "$ScaleLogPath" ] \
		|| fail "the plan's landed commit changes $(joined_paths "${changed[@]}") beyond merging $(short "$LAND_HEAD") onto $(short "$LAND_TIP") and minting the scale row in $ScaleLogPath — nothing is pushed"
	require_minted_rows_only "$merged"
}

remerged_candidate() {
	local tip="$1" merged

	case "$LAND_KIND" in
		"$KindOrdinary") merge_onto "$tip" "$LAND_TIP" "$LAND_LANDED" ;;
		"$KindChain")
			merged="$(chain_merged_onto "$tip" "$LAND_BASE" "$LAND_LANDED")"
			make_pin "$merged" "$LAND_LANDED" "$LAND_TAG"
			;;
		*) fail "no re-merge for a $LAND_KIND land" ;;
	esac
}

push_landing() {
	local planned="$1" push tip candidate now

	for ((push = 1; ; push++)); do
		tip="$(standing_tip "$LAND_MODE" "$MomentPush")"

		if [ "$tip" = "$LAND_TIP" ]; then
			candidate="$planned"
		elif seed_inputs_differ "$LAND_TIP" "$tip"; then
			redispatch "$tip"
			return 0
		else
			candidate="$(remerged_candidate "$tip")"
		fi

		case "$LAND_MODE" in
			"$ModeRehearsal")
				bundle_commit "$candidate" "$tip" "$WouldPushRef" "$WouldPushBundle"
				record_landed "$LAND_MODE" "$candidate"
				return 0
				;;
			"$ModeMain")
				if git push --quiet "--force-with-lease=refs/heads/$Branch:$tip" "$Remote" "$candidate:refs/heads/$Branch"; then
					record_landed "$LAND_MODE" "$candidate"
					return 0
				fi

				now="$(main_tip)"
				[ "$now" != "$tip" ] || fail "$Remote refused the push of $(short "$candidate") to $Branch, which is still at $(short "$tip")"
				[ "$push" -lt "$MaxPushAttempts" ] || fail "$Remote/$Branch moved under each of $MaxPushAttempts pushes — submit again"
				echo "land.sh: $Remote/$Branch moved past $(short "$tip") during the push — merging again (push $((push + 1)) of $MaxPushAttempts)" >&2
				;;
			*) fail "no land in $LAND_MODE mode" ;;
		esac
	done
}

land_ordinary() {
	receive_landed
	require_landed_as_planned
	push_landing "$LAND_LANDED"
}

land_chain() {
	local merged pin

	require_env LAND_TAG LAND_MERGED LAND_PIN
	recreate_landed
	merged="$(chain_merged_onto "$LAND_TIP" "$LAND_BASE" "$LAND_LANDED")"
	[ "$merged" = "$LAND_MERGED" ] || fail "the chain's $(short "$LAND_LANDED") merged onto $(short "$LAND_TIP") came out as $merged, not the pin job's $LAND_MERGED — nothing is pushed"
	pin="$(make_pin "$merged" "$LAND_LANDED" "$LAND_TAG")"
	[ "$pin" = "$LAND_PIN" ] || fail "the pin commit came out as $pin, not the pin job's $LAND_PIN — nothing is pushed"
	require_still_published
	push_landing "$LAND_PIN"
}

command_land() {
	require_env LAND_KIND LAND_HEAD LAND_RUN_HEAD LAND_MODE LAND_TIP LAND_LANDED LAND_COMMITTED LAND_BRANCH
	[ "$LAND_HEAD" = "$LAND_RUN_HEAD" ] || fail "the plan names $(short "$LAND_HEAD") as the head, and this run lands $(short "$LAND_RUN_HEAD") — nothing is pushed"
	require_attempt
	resolve_commit "$LAND_TIP" > /dev/null

	case "$LAND_KIND" in
		"$KindOrdinary") land_ordinary ;;
		"$KindChain") land_chain ;;
		*) fail "no land for a $LAND_KIND plan" ;;
	esac
}

command_consume() {
	local target

	require_env LAND_BRANCH LAND_RUN_HEAD
	[ "$LAND_BRANCH" != "$Branch" ] || fail "a land run never deletes $Branch"

	if git push --quiet "--force-with-lease=refs/heads/$LAND_BRANCH:$LAND_RUN_HEAD" "$Remote" ":refs/heads/$LAND_BRANCH"; then
		summary "deleted $LAND_BRANCH at $(short "$LAND_RUN_HEAD")"
		return 0
	fi

	target="$(remote_ref_target "refs/heads/$LAND_BRANCH")" || exit 1

	case "$target" in
		"") summary "$LAND_BRANCH is already gone" ;;
		"$LAND_RUN_HEAD") fail "deleting $LAND_BRANCH failed — re-run this job" ;;
		*) summary "$LAND_BRANCH was pushed again, to $(short "$target") — kept" ;;
	esac
}

delete_remote_ref() {
	local ref="$1" expected="$2" target

	target="$(remote_ref_target "$ref")" || exit 1

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
	local pins landed="${LAND_LANDED:-}" base

	require_env LAND_HEAD LAND_TAG
	fetch_branch || fail "$Remote/$Branch cannot be read — nothing is retracted; re-run this job"
	pins="$(git log --format=%H -S"$LAND_TAG" "$Remote/$Branch" -- "$SeedPinFile")" \
		|| fail "git cannot read the history of $SeedPinFile on $Remote/$Branch — nothing is retracted; re-run this job"

	if [ -n "$pins" ]; then
		summary "$Remote/$Branch pins $LAND_TAG in $(short "$pins") — it stays published"
		return 0
	fi

	if [ -z "$landed" ]; then
		landed="$(remote_ref_target "refs/tags/$LAND_TAG")" || exit 1

		if [ -n "$landed" ]; then
			resolve_commit "$landed" "the tag $LAND_TAG names it, so nothing is retracted; re-run this job" > /dev/null
			resolve_commit "$LAND_HEAD" "it is this run's head, so nothing is retracted; re-run this job" > /dev/null
			base="$(git rev-parse --verify "$landed^")"
			require_seed_of_head "$LAND_TAG" "$LAND_HEAD" "$landed" "$base" "$LAND_TAG is left as it is"
		fi
	fi

	if seed_published_on "$LAND_TAG" "$landed"; then
		seed_release_delete || fail "deleting the release $LAND_TAG failed — re-run this job"
		summary "deleted the release $LAND_TAG"
	elif seed_release_published "$LAND_TAG"; then
		fail "the release $LAND_TAG is not on this run's $(short "$landed") — left as it is"
	fi

	delete_remote_ref "refs/tags/$LAND_TAG" "$landed"
	delete_remote_ref "refs/tags/$LAND_TAG$ChainRefSuffix" "$LAND_HEAD"
}

run_jobs() {
	gh api "$SeedRepoApi/actions/runs/$1/jobs?per_page=100" --jq "$2"
}

newest_planned_run() {
	local runs="$1" line run plan

	while IFS= read -r line; do
		[ -n "$line" ] || continue
		run="${line%%$'\t'*}"
		plan="$(run_jobs "$run" ".jobs[] | select(.name == \"$JobPlan\") | .conclusion // \"\"")" \
			|| fail "GitHub cannot say what the $LandWorkflow run $run did"

		case "$plan" in
			"" | "$JobSkipped") ;;
			*)
				planned_run="$line"
				return 0
				;;
		esac
	done < <(printf '%s\n' "$runs")

	return 1
}

run_annotations() {
	local run="$1" jobs job

	jobs="$(run_jobs "$run" '.jobs[].id')" \
		|| fail "GitHub cannot list the jobs of the $LandWorkflow run $run"

	for job in $jobs; do
		gh api "$SeedRepoApi/check-runs/$job/annotations?per_page=100" \
			--jq '.[] | "\(.annotation_level)\t\(.title // "")\t\(.message | gsub("\n"; " "))"' \
			|| fail "GitHub cannot list the annotations of the job $job of the $LandWorkflow run $run"
	done
}

annotation_messages() {
	printf '%s\n' "$1" | awk -F '\t' -v level="$2" -v title="$3" '$1 == level && $2 == title { print $3 }'
}

any_run_unfinished() {
	printf '%s\n' "$1" | awk -F '\t' -v done="$RunCompleted" 'NF && $2 != done { unfinished = 1 } END { exit !unfinished }'
}

command_await() {
	local branch="${1:?land.sh await needs the branch}" head="${2:?land.sh await needs the head commit}" after=0 argument
	local runs run conclusion url annotations recorded redispatched failures=0 finds=0 bounce planned_run mode words title verb

	shift 2

	for argument in "$@"; do
		case "$argument" in
			--after=*) after="${argument#--after=}" ;;
			*) fail "unknown argument to await: $argument" ;;
		esac
	done

	case "$after" in
		"" | *[!0-9]*) fail "--after takes a run id, not $after" ;;
		*) ;;
	esac

	head="$(git rev-parse --verify "$head^{commit}")"

	while true; do
		if ! runs="$(gh api "$SeedRepoApi/actions/workflows/$LandWorkflow/runs?branch=$branch&per_page=100" \
			--jq ".workflow_runs[] | select(.id > $after and (.head_sha == \"$head\" or (.display_title | startswith(\"$RunNamePrefix$head \")))) | \"\(.id)\t\(.status)\t\(.conclusion // \"\")\t\(.html_url)\"")"; then
			failures=$((failures + 1))
			[ "$failures" -lt "$MaxSubmitPollFailures" ] || fail "the $LandWorkflow runs of $branch cannot be read ($failures consecutive failures) — it may still land; follow it on GitHub"
			sleep "$SubmitPollSeconds"
			continue
		fi

		failures=0

		if any_run_unfinished "$runs"; then
			sleep "$SubmitPollSeconds"
			continue
		fi

		if ! newest_planned_run "$runs"; then
			finds=$((finds + 1))
			[ "$finds" -lt "$SubmitFindAttempts" ] || fail "no $LandWorkflow run planned a land of $(short "$head") on $branch"
			sleep "$SubmitPollSeconds"
			continue
		fi

		finds=0
		IFS=$'\t' read -r run _ conclusion url <<< "$planned_run"
		annotations="$(run_annotations "$run")"
		redispatched="$(annotation_messages "$annotations" "$AnnotationNotice" "$RedispatchedTitle")"
		recorded=""

		for mode in "$ModeMain" "$ModeRehearsal"; do
			words="$(landing_words "$mode")"
			IFS=$'\t' read -r title verb <<< "$words"
			recorded="$(annotation_messages "$annotations" "$AnnotationNotice" "$title")"

			[ -z "$recorded" ] || break
		done

		if [ -n "$recorded" ] && [ "$conclusion" = "$RunSuccess" ]; then
			echo "land.sh: $LandWorkflow ($mode) of $(short "$head") from $branch: $Branch $verb $recorded: $url" >&2
			printf '%s\n' "$recorded"
			return 0
		fi

		if [ -n "$redispatched" ]; then
			echo "land.sh: $url re-dispatched the land ($redispatched) — following it" >&2
			after="$run"
			sleep "$SubmitPollSeconds"
			continue
		fi

		if [ "$conclusion" = "$RunSuccess" ]; then
			echo "land.sh: $LandWorkflow succeeded and pushed nothing to $Branch: $url" >&2
			return 0
		fi

		bounce="$(annotation_messages "$annotations" "$AnnotationFailureLevel" "$FailureTitle")"
		[ -z "$bounce" ] || printf '%s\n' "$bounce" | sed 's/^/land.sh: bounced: /' >&2
		[ -z "$recorded" ] || fail "$LandWorkflow ended $conclusion after recording that $Branch $verb $recorded ($mode): $url"
		fail "$LandWorkflow ended $conclusion: $url"
	done
}

command_submit() {
	local mode="$ModeMain" name="" argument head trailer branch after

	for argument in "$@"; do
		case "$argument" in
			--rehearsal) mode="$ModeRehearsal" ;;
			--name=*) name="${argument#--name=}" ;;
			*) fail "unknown argument to submit: $argument" ;;
		esac
	done

	if [ -z "$name" ]; then
		name="$(git symbolic-ref --quiet --short HEAD)" || fail "HEAD is detached — name the land with --name=<name>"
		[ "$name" != "$Branch" ] || fail "HEAD is on $Branch — name the land with --name=<name>"
	fi

	branch="$SubmitBranchPrefix$name"
	git check-ref-format "refs/heads/$branch" || fail "$branch is not a branch name"
	head="$(git rev-parse HEAD)"
	trailer="$(commit_trailer "$head" "$LandTrailer")"
	[ -z "$trailer" ] || fail "HEAD carries a \`$LandTrailer:\` trailer, so pushing it starts the land by itself — push the branch instead"

	after="$(gh api "$SeedRepoApi/actions/workflows/$LandWorkflow/runs?per_page=1" --jq '.workflow_runs[0].id // 0')" \
		|| fail "GitHub cannot list the $LandWorkflow runs"
	git push --quiet "$Remote" "$head:refs/heads/$branch" \
		|| fail "$Remote refused $branch — the name still exists: a run of it is going, or a run kept it; read the run, or submit under another --name"
	dispatch_land "$branch" "$mode" "$head" "$FirstLandAttempt"
	echo "land.sh: $LandWorkflow is landing $(short "$head") ($mode) from $branch" >&2

	command_await "$branch" "$head" "--after=$after"

	if [ "$mode" = "$ModeMain" ]; then
		echo "land.sh: $Remote/$Branch now holds the landing; to follow it: git fetch $Remote $Branch && git reset --keep $Remote/$Branch" >&2
	fi
}

subcommand="${1:-}"
[ "$#" -eq 0 ] || shift

case "$subcommand" in
	submit) command_submit "$@" ;;
	await) command_await "$@" ;;
	chain) command_chain "$@" ;;
	plan) command_plan "$@" ;;
	freeze) command_freeze ;;
	prove) command_prove "$@" ;;
	publish) command_publish ;;
	pin) command_pin ;;
	land) command_land ;;
	consume) command_consume ;;
	retract) command_retract ;;
	*) fail "usage: land.sh <submit|await|chain|plan|freeze|prove|publish|pin|land|consume|retract> (docs/RELEASING.md)" ;;
esac
