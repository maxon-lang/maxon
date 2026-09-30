#!/usr/bin/env bash

set -euo pipefail

MaxAttempts=5
BotName="github-actions[bot]"
BotEmail="41898282+github-actions[bot]@users.noreply.github.com"
Branch="main"
# Every path here is a CI input, so a later commit that changed one has a run of its own, and that
# run's goldens are the ones `main` should hold.
GoldenInputs=(maxon-bin stdlib runtime 'specs/*.md')

[ "$#" -eq 2 ] || { echo "usage: scripts/record-goldens.sh <run-sha> <artifacts-dir>" >&2; exit 2; }

run_sha="$1"
artifacts=""
[ ! -d "$2" ] || artifacts="$(cd "$2" && pwd)"

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
. scripts/lib/goldens.sh

if [ -z "$artifacts" ] || [ -z "$(ls -A "$artifacts")" ]; then
	echo "record-goldens.sh: no goldens artifacts; nothing to record"
	exit 0
fi

git cat-file -e "$run_sha^{commit}" || { echo "record-goldens.sh: $run_sha is not a commit in this clone" >&2; exit 1; }
goldens_guard_artifacts "$artifacts" || exit 1

short_sha="$(git rev-parse --short "$run_sha")"
message="$(mktemp)"
trap 'rm -f "$message"' EXIT

for attempt in $(seq 1 "$MaxAttempts"); do
	git fetch --quiet origin "$Branch"

	inputs_status=0
	git diff --quiet "$run_sha" "origin/$Branch" -- "${GoldenInputs[@]}" || inputs_status=$?

	case "$inputs_status" in
		0) ;;
		1)
			echo "::notice::record-goldens.sh: origin/$Branch changed the compiler or the specs since $short_sha; the run for that newer commit records its own goldens"
			exit 0
			;;
		*)
			echo "record-goldens.sh: comparing $short_sha with origin/$Branch failed (git diff exited $inputs_status)" >&2
			exit 1
			;;
	esac

	git checkout --quiet --force --detach "origin/$Branch"
	git clean -fdq -- "$GOLDENS_ROOT"
	changes="$(goldens_place "$artifacts" . | awk '$1 != "unchanged"')"

	if [ -z "$changes" ]; then
		echo "record-goldens.sh: origin/$Branch already holds every golden $short_sha produced"
		exit 0
	fi

	git add -- "$GOLDENS_ROOT"
	tally="$(printf '%s\n' "$changes" | goldens_tally)"
	printf 'goldens recorded by CI for %s\n\n%s\n' "$short_sha" "$tally" > "$message"

	git -c user.name="$BotName" -c user.email="$BotEmail" commit --quiet -F "$message"

	if git push origin "HEAD:$Branch"; then
		echo "record-goldens.sh: pushed $(git rev-parse --short HEAD) on attempt $attempt"
		printf '%s\n' "$tally"
		exit 0
	fi

	echo "record-goldens.sh: push rejected on attempt $attempt of $MaxAttempts" >&2
done

echo "record-goldens.sh: gave up after $MaxAttempts rejected pushes" >&2
exit 1
