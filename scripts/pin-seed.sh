#!/usr/bin/env bash
#
# Publish the seed `scripts/freeze-seed.sh` wrote to `temp/seed/dist/` and pin `main` to it,
# deleting every `scripts/seed-shim/` patch. It is CI's `pin` job, run once `prove` has built the
# tree with every archive and no shim.
#
# Usage:
#   scripts/pin-seed.sh --sha=<the commit the seed was frozen from> --tag=<its seed tag>

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/seed.sh

Remote="origin"
Branch="main"
MaxPushAttempts=3
BotName="github-actions[bot]"
BotEmail="41898282+github-actions[bot]@users.noreply.github.com"
PinWorkflow="ci.yml"

sha=""
tag=""

for argument in "$@"; do
	case "$argument" in
		--sha=?*) sha="${argument#--sha=}" ;;
		--tag=?*) tag="${argument#--tag=}" ;;
		*) echo "pin-seed.sh: unknown argument: $argument" >&2; exit 2 ;;
	esac
done

[[ "$sha" =~ ^[0-9a-f]{40}$ ]] || { echo "pin-seed.sh: --sha=<the 40-digit commit the seed was frozen from> is required" >&2; exit 2; }
[ -n "$tag" ] || { echo "pin-seed.sh: --tag=<the tag freeze-seed.sh named the seed> is required" >&2; exit 2; }

seed_tag_names_commit "$tag" "$sha" \
	|| { echo "pin-seed.sh: $tag is not a seed tag frozen from $sha" >&2; exit 1; }

seed_release_create_command "$tag" "$sha"

git cat-file -e "$sha^{commit}" 2>/dev/null || { echo "pin-seed.sh: $sha is not in this checkout — fetch full history" >&2; exit 1; }

bot_git() { git -c user.name="$BotName" -c user.email="$BotEmail" "$@"; }

fetch_branch() {
	git fetch --quiet "$Remote" "+refs/heads/$Branch:refs/remotes/$Remote/$Branch"
}

superseded_head=""

# The patches this pin deletes were judged against the frozen commit. A later commit on `main` that
# changed them or the pin runs CI of its own, and that run decides the next seed.
pin_inputs_superseded() {
	local changed

	superseded_head="$(git rev-parse --verify "$Remote/$Branch^{commit}")" \
		|| { echo "pin-seed.sh: cannot resolve $Remote/$Branch" >&2; exit 1; }

	[ "$superseded_head" != "$sha" ] || return 1

	changed="$(git diff --name-only "$sha" "$superseded_head" -- "$SeedShimDirectory" "$SeedPinFile")" \
		|| { echo "pin-seed.sh: cannot list what $Remote/$Branch changed since $sha" >&2; exit 1; }

	[ -n "$changed" ]
}

require_unpublished() {
	local remote_tag

	if seed_release_published "$tag"; then
		echo "pin-seed.sh: the release $tag already exists — a seed tag never moves" >&2
		exit 1
	fi

	remote_tag="$(git ls-remote --tags "$Remote" "refs/tags/$tag")"
	[ -z "$remote_tag" ] || { echo "pin-seed.sh: $Remote already has the tag $tag — a seed tag never moves" >&2; exit 1; }
}

commit_pin() {
	local patch

	git checkout --quiet --detach "$Remote/$Branch"

	printf '%s\n' "$tag" > "$SeedPinFile"
	git add -- "$SeedPinFile"

	while IFS= read -r patch; do
		[ -n "$patch" ] || continue
		git rm --quiet -- "$patch"
	done < <(seed_shim_patches)

	bot_git commit --quiet -m "the tree is built with $tag, frozen from ${sha:0:$SeedCommitAbbreviation}, and the seed-shim patches it makes dead are gone"
}

fetch_branch

if pin_inputs_superseded; then
	echo "::notice::pin-seed.sh: $tag (frozen from $sha) is superseded — $Remote/$Branch is at $superseded_head and changed $SeedShimDirectory or $SeedPinFile, so that commit's own run decides the next seed; nothing was published or pushed"
	exit 0
fi

require_unpublished

echo "pin-seed.sh: publishing $tag"
"${seed_release_create[@]}"

commit_pin

attempt=1
until git push --quiet "$Remote" "HEAD:refs/heads/$Branch"; do
	if [ "$attempt" -ge "$MaxPushAttempts" ]; then
		echo "pin-seed.sh: $tag is published but $Remote/$Branch refused the pin $MaxPushAttempts times" >&2
		exit 1
	fi

	attempt=$((attempt + 1))
	echo "pin-seed.sh: push refused — rebasing onto $Remote/$Branch (attempt $attempt of $MaxPushAttempts)" >&2

	fetch_branch

	if pin_inputs_superseded; then
		echo "pin-seed.sh: $tag is published but $Remote/$Branch moved to $superseded_head and changed $SeedShimDirectory or $SeedPinFile — the release is orphaned and $tag is not pinned" >&2
		exit 1
	fi

	bot_git rebase --quiet "$Remote/$Branch"
done

echo "pin-seed.sh: $Branch is pinned to $tag"

# GitHub starts no workflow for a push made with `GITHUB_TOKEN`, so the pinned commit's CI is
# dispatched.
gh workflow run "$PinWorkflow" --ref "$Branch"
