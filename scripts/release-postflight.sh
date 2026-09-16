#!/bin/sh
#
# Every question a release must answer AFTER `release.yml`'s `publish` job has run. It reads and
# reports; it edits no release, writes no file into the tree and publishes nothing.
#
# ⭐⭐ **A JOB CONCLUSION IS NOT EVIDENCE, AND THAT IS THE WHOLE REASON THIS EXISTS.** Every check here
# inspects the ARTIFACT — the release's own assets, the redirect an installer resolves, the bytes the
# site actually serves — because the ways a release goes wrong are precisely the ways a workflow stays
# green: a job gated `needs:` SKIPS when its build fails and the run still reports success, and a step
# missing a credential echoes a notice and exits 0. A green run has said nothing about either.
#
# ⛔ **THE WHOLE LIST RUNS AND EVERY ANSWER IS PRINTED.** A published release is already public; what
# is wanted is every defect at once, so one round of repair covers them all.
#
# Usage:
#   scripts/release-postflight.sh <X.Y.Z>
#
#   <X.Y.Z>   the published version, with or without a leading `v`
#
# Non-zero when any check FAILED. A SKIPPED check tested nothing, is not a pass, and is named again in
# the summary.

set -eu

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"
# The library is resolved against the repository root, which shellcheck has no way to know.
# shellcheck disable=SC1091
. scripts/lib/release-checks.sh

Repo="maxon-lang/maxon"
ReleasesUrl="https://github.com/$Repo/releases"
SiteUrl="https://maxon.dev"
ChecksumAsset="SHA256SUMS"

# The targets `release.yml` builds, one runner each. The two installers are served verbatim from
# `PublicDir` and `maxon upgrade` fetches them under exactly these names.
ReleaseTargets="x64-windows x64-linux arm64-linux arm64-macos"
InstallScripts="install.sh install.ps1"
PublicDir="website/public"

DeployJob="deploy"
DeployStep="Deploy to Cloudflare Pages"
DeploySuccessMarker="Deployment complete"
DeployNoCredentialMarker="::notice::no CLOUDFLARE_API_TOKEN"
DownstreamWorkflows="homebrew docker vscode-extension website install-script"

version=""

for arg in "$@"; do
	case "$arg" in
		-h|--help) sed -n '2,21p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
		-*)        echo "release-postflight.sh: unknown argument '$arg' (see --help)" >&2; exit 2 ;;
		*)         version="$arg" ;;
	esac
done

[ -n "$version" ] || { echo "release-postflight.sh: pass the published version, e.g. scripts/release-postflight.sh 0.2.2" >&2; exit 2; }
version="$(check_release_version "$version" release-postflight.sh)" || exit 2

tag="v$version"

# `announce.sh` writes each release's post as `maxon-X-Y-Z`, so the slug takes dashes where the
# version takes dots.
post_slug="maxon-$(printf '%s' "$version" | tr '.' '-')"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT INT TERM

gh_unavailable="$(check_gh_reason)"

# ⛔ EVERY WORKFLOW QUESTION IS ABOUT RUNS AT THE TAG'S COMMIT — `publish` starts them with `--ref`, so
# that commit is the only thing they have in common. The local tag answers offline; `gh` answers when
# the tag has not been fetched.
tag_commit=""
if git rev-parse -q --verify "refs/tags/$tag^{commit}" >/dev/null 2>&1; then
	tag_commit="$(git rev-parse "refs/tags/$tag^{commit}")"
elif [ -z "$gh_unavailable" ]; then
	tag_commit="$(gh api "repos/$Repo/commits/$tag" --jq .sha 2>/dev/null || true)"

	# ⛔ `gh api` PRINTS THE ERROR BODY ON STDOUT, so an unresolved tag arrives as a JSON object that
	# reads as an answer. Anything but a hex sha is no answer at all.
	case "$tag_commit" in
		"" | *[!0-9a-f]*) tag_commit="" ;;
	esac
fi

printf 'release-postflight.sh: %s\n\n' "$tag"

asset_name() {
	case "$1" in
		*-windows) printf 'maxon-%s-%s.zip' "$version" "$1" ;;
		*)         printf 'maxon-%s-%s.tar.gz' "$version" "$1" ;;
	esac
}

# `-L` because every URL here is behind at least one redirect; the body is kept so a check can read it.
# A transport failure prints `000` rather than nothing, so the reason a check gives always names a
# status a reader can act on.
http_status_into() {
	http_status_code="$(curl -sSL --proto '=https' --tlsv1.2 -o "$2" -w '%{http_code}' "$1" 2>"$work/curl.err" || true)"

	if [ -z "$http_status_code" ]; then
		http_status_code="000"
	fi

	printf '%s' "$http_status_code"
}

# ── the release itself ────────────────────────────────────────────────────────────────────────────

# ⛔ FIVE ASSETS, NO MORE AND NO FEWER. Both installers download one archive and `SHA256SUMS` and
# verify one against the other, so a missing checksum file fails every install and a missing archive
# fails one platform's — and a matrix leg that died leaves exactly that shape.
if [ -n "$gh_unavailable" ]; then
	check_skip release-assets "$gh_unavailable"
elif ! gh release view "$tag" --json assets --jq '.assets[].name' > "$work/assets" 2>"$work/assets.err"; then
	check_fail release-assets "gh cannot read a release $tag"
	check_detail "$work/assets.err"
else
	missing=""
	for target in $ReleaseTargets; do
		want="$(asset_name "$target")"
		if ! grep -Fx -- "$want" "$work/assets" >/dev/null; then
			missing="$missing $want"
		fi
	done
	if ! grep -Fx -- "$ChecksumAsset" "$work/assets" >/dev/null; then
		missing="$missing $ChecksumAsset"
	fi

	# An asset nobody expected is as much a defect as a missing one: it means the release carries
	# something no installer, formula or Dockerfile knows how to read.
	unexpected=""
	while read -r name; do
		[ -n "$name" ] || continue
		case "$name" in
			"$ChecksumAsset") continue ;;
		esac
		known=0
		for target in $ReleaseTargets; do
			if [ "$name" = "$(asset_name "$target")" ]; then
				known=1
			fi
		done
		if [ "$known" -eq 0 ]; then
			unexpected="$unexpected $name"
		fi
	done < "$work/assets"

	if [ -n "$missing" ]; then
		check_fail release-assets "$tag is missing:$missing"
	elif [ -n "$unexpected" ]; then
		check_fail release-assets "$tag carries assets nothing downstream reads:$unexpected"
	else
		check_pass release-assets "the four archives and $ChecksumAsset are attached"
	fi
fi

# ⛔ **THIS REDIRECT IS WHAT DECIDES WHICH RELEASE USERS GET.** Both one-line installers resolve
# `releases/latest` to learn the newest version, so a release that did not become `latest` is a
# release nobody installs however complete its assets are.
latest_target="$(curl -sS --proto '=https' --tlsv1.2 -o /dev/null -w '%{redirect_url}' "$ReleasesUrl/latest" 2>"$work/latest.err" || true)"
if [ "$latest_target" = "$ReleasesUrl/tag/$tag" ]; then
	check_pass latest-redirect "releases/latest resolves to $tag"
elif [ -z "$latest_target" ]; then
	check_fail latest-redirect "releases/latest returned no redirect"
	check_detail "$work/latest.err"
else
	check_fail latest-redirect "releases/latest resolves to $latest_target, not $tag — the installers take that one"
fi

# ── the site the release notes link to ────────────────────────────────────────────────────────────

# ⛔ THE PUBLISHED NOTES LINK TO THIS POST BY NAME, and notes published once cannot be corrected into
# truth by a later deploy.
post_url="$SiteUrl/blog/$post_slug/"
post_status="$(http_status_into "$post_url" /dev/null)"
if [ "$post_status" = 200 ]; then
	check_pass blog-post "$post_url"
else
	check_fail blog-post "$post_url returns $post_status — the release notes link to it"
	check_detail "$work/curl.err"
fi

# The site's changelog page carries every release, so this release's own number appearing in it is
# what says the deploy built from the tag rather than from something older.
changelog_url="$SiteUrl/docs/changelog/"
changelog_status="$(http_status_into "$changelog_url" "$work/changelog.html")"
if [ "$changelog_status" != 200 ]; then
	check_fail changelog-page "$changelog_url returns $changelog_status"
	check_detail "$work/curl.err"
elif grep -F -- "$version" "$work/changelog.html" >/dev/null; then
	check_pass changelog-page "$changelog_url names $version"
else
	check_fail changelog-page "$changelog_url is served but does not name $version — it was built before the tag"
fi

# ⛔ **THE SERVED INSTALLERS PROVE THE DEPLOY HAPPENED, AND NOTHING ELSE DOES.** `maxon upgrade` fetches
# these at run time from every compiler already shipped, so a stale copy breaks upgrading everywhere
# at once — silently, because the old script still installs the old release perfectly well.
for name in $InstallScripts; do
	label="served-$name"
	tag_copy="$work/tag-$name"
	live_copy="$work/live-$name"

	if git cat-file blob "$tag:$PublicDir/$name" > "$tag_copy" 2>/dev/null; then
		source_known=1
	elif [ -z "$gh_unavailable" ] && gh api "repos/$Repo/contents/$PublicDir/$name?ref=$tag" -H "Accept: application/vnd.github.raw" > "$tag_copy" 2>/dev/null; then
		source_known=1
	else
		source_known=0
	fi

	if [ "$source_known" -eq 0 ]; then
		check_skip "$label" "no copy of $PublicDir/$name at $tag here — run: git fetch origin --tags"
	else
		served_status="$(http_status_into "$SiteUrl/$name" "$live_copy")"

		if [ "$served_status" != 200 ]; then
			check_fail "$label" "$SiteUrl/$name returns $served_status"
			check_detail "$work/curl.err"
		elif cmp -s "$tag_copy" "$live_copy"; then
			check_pass "$label" "$SiteUrl/$name is the copy at $tag, byte for byte"
		else
			check_fail "$label" "$SiteUrl/$name differs from $PublicDir/$name at $tag — the deploy did not reach the site"
			check_note "Every shipped compiler's \`maxon upgrade\` fetches this file at run time."
		fi
	fi
done

# ── the workflows the tag started ─────────────────────────────────────────────────────────────────

# Newest run of one workflow at the tag's commit, as `<status> <conclusion> <databaseId> <url>`. Empty
# output means no such run, which downstream of a release is a defect rather than an absence.
run_at_tag() {
	gh run list --commit "$tag_commit" --workflow="$1.yml" --limit 1 \
		--json status,conclusion,databaseId,url \
		--jq '.[] | "\(.status) \(.conclusion) \(.databaseId) \(.url)"'
}

run_field() { printf '%s' "$1" | cut -d' ' -f"$2"; }

# ⛔ **THE `deploy` JOB'S CONCLUSION IS EXACTLY THE WRONG THING TO READ.** It is gated `needs: build`,
# so a failed build SKIPS it while the run reports success; and the deploy step itself echoes a
# `::notice::` and exits 0 when a Cloudflare credential is absent, so the job is green having deployed
# nothing. The step's own log is the only place the deployment is recorded.
if [ -n "$gh_unavailable" ]; then
	check_skip website-deployed "$gh_unavailable"
elif [ -z "$tag_commit" ]; then
	check_skip website-deployed "$tag does not resolve to a commit here — run: git fetch origin --tags"
elif ! website_run="$(run_at_tag website 2>"$work/website.err")"; then
	check_fail website-deployed "gh could not list website runs at $tag"
	check_detail "$work/website.err"
elif [ -z "$website_run" ]; then
	check_fail website-deployed "no website run at $tag — publish starts it, so a missing run means it never did"
else
	website_run_id="$(run_field "$website_run" 3)"
	website_run_url="$(run_field "$website_run" 4)"

	deploy_job="$(gh run view "$website_run_id" --json jobs \
		--jq ".jobs[] | select(.name == \"$DeployJob\") | \"\(.conclusion) \(.databaseId)\"" 2>/dev/null || true)"

	deploy_conclusion="$(run_field "$deploy_job" 1)"

	if [ -z "$deploy_job" ]; then
		check_fail website-deployed "the website run at $tag has no $DeployJob job — $website_run_url"
	elif [ "$deploy_conclusion" = skipped ]; then
		check_fail website-deployed "$DeployJob SKIPPED — its build failed and the run still reports success — $website_run_url"
	elif [ "$deploy_conclusion" = null ]; then
		check_skip website-deployed "$DeployJob has not finished — $website_run_url"
	elif ! gh run view --job "$(run_field "$deploy_job" 2)" --log > "$work/deploy.log" 2>"$work/deploy.err"; then
		check_skip website-deployed "the run's logs are gone, so the deploy step's own output cannot be read — $website_run_url"
		check_detail "$work/deploy.err"
	else
		# ⛔ **A JOB LOG CARRIES EACH STEP'S SOURCE AS WELL AS ITS OUTPUT, AND THE SOURCE CONTAINS THE
		# NO-CREDENTIAL NOTICE VERBATIM** — matched there it would report what the step says it WOULD do
		# rather than what it did. A runner brackets every echoed script in `##[group]`/`##[endgroup]`,
		# which is what separates the two; the colouring around it is not, because it reaches this host
		# as the two literal characters `^[`.
		#
		# ⚠ THE MARKERS ARE SOUGHT ACROSS THE WHOLE JOB, NOT WITHIN A NAMED STEP: `gh` renders some
		# runs' logs with every line attributed to `UNKNOWN STEP`, so a step-name filter matches nothing
		# and reports a deploy that happened as one that did not. Within this job only wrangler emits
		# either marker.
		awk '/##\[group\]/ { inside = 1 } !inside { print } /##\[endgroup\]/ { inside = 0 }' \
			"$work/deploy.log" > "$work/deploy.out"

		if grep -F -- "$DeploySuccessMarker" "$work/deploy.out" >/dev/null; then
			check_pass website-deployed "$DeployStep reported '$DeploySuccessMarker'"
		elif grep -F -- "$DeployNoCredentialMarker" "$work/deploy.out" >/dev/null; then
			check_fail website-deployed "$DeployStep found no Cloudflare credential and deployed nothing — $website_run_url"
		else
			check_fail website-deployed "$DeployStep never reported '$DeploySuccessMarker' — $website_run_url"
		fi
	fi
fi

# ⛔ **A MISSING RUN IS A FAILURE HERE, NOT AN ABSENCE.** A release created with `GITHUB_TOKEN` fires no
# `release: published` event, so nothing downstream starts by itself — `publish` dispatches all five at
# the tag. Nothing having run means that step did not do its job, and Homebrew, the image, the
# extension, the site and the install-script lint are all still describing the previous release.
for workflow in $DownstreamWorkflows; do
	label="workflow-$workflow"

	if [ -n "$gh_unavailable" ]; then
		check_skip "$label" "$gh_unavailable"
	elif [ -z "$tag_commit" ]; then
		check_skip "$label" "$tag does not resolve to a commit here — run: git fetch origin --tags"
	elif ! workflow_run="$(run_at_tag "$workflow" 2>"$work/run.err")"; then
		check_fail "$label" "gh could not list $workflow runs at $tag"
		check_detail "$work/run.err"
	elif [ -z "$workflow_run" ]; then
		check_fail "$label" "MISSING — no $workflow run at $tag; publish dispatches it, so it never started"
	else
		run_status="$(run_field "$workflow_run" 1)"
		run_conclusion="$(run_field "$workflow_run" 2)"
		run_url="$(run_field "$workflow_run" 4)"

		if [ "$run_status" != completed ]; then
			check_skip "$label" "still $run_status — $run_url"
		elif [ "$run_conclusion" = success ]; then
			check_pass "$label" "ran at $tag and succeeded"
		else
			check_fail "$label" "$run_conclusion — $run_url"
		fi
	fi
done

if ! check_summary release-postflight.sh; then
	exit 1
fi
