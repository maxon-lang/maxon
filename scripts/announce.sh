#!/usr/bin/env bash
#
# Announce a published release on maxon.dev.
#
# ⭐⭐ **IT RUNS BY HAND, AFTER THE RELEASE IS LIVE, AND THAT IS WHAT MAKES IT NON-FATAL.** The web
# repository needs push credentials the compiler repository's CI does not have and should not be
# given, and the publish runner has no `maxon-web` checkout. A step that runs AFTER the release exists
# cannot fail a release that has already happened — which is a stronger guarantee than a `|| true`
# inside `release.sh` burying a warning in a log nobody reads.
#
# ⭐ **THE CHANGES COME FROM `CHANGELOG.md`.** The post, the site's changelog page, the GitHub release
# notes and the marketplace tab are then four renderings of one source, and cannot come to disagree
# about what shipped.
#
# ⚠ **IT WRITES THREE FILES IN ONE COMMIT, AND THEY ARE THREE DIFFERENT JOBS.** The blog post
# announces THIS release and is never touched again; the changelog page carries EVERY release and is
# rewritten each time; `src/version.ts` is what the download links are built from, so a release that
# skipped it would leave every download button pointing at the previous release's filenames.
#
# Usage:
#   scripts/announce.sh <version> [--web-dir=<path>] [--dry-run]
#
#   <version>     the released version, without a leading `v` — e.g. 0.1.1
#   --web-dir=    the maxon-web checkout. Defaults to $MAXON_WEB_DIR, else ../maxon-web
#   --dry-run     print both files to stdout, write and push nothing

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"

ReleasesUrl="https://github.com/maxon-lang/maxon/releases"
BlogSubdir="src/content/docs/blog"
ChangelogPagePath="src/content/docs/docs/changelog.md"
VersionModulePath="src/version.ts"
ChangelogFile="CHANGELOG.md"
WebDefaultBranch="main"

version=""
web_dir="${MAXON_WEB_DIR:-../maxon-web}"
dry_run=0

for arg in "$@"; do
	case "$arg" in
		--web-dir=*) web_dir="${arg#--web-dir=}" ;;
		--dry-run)   dry_run=1 ;;
		--help|-h)   sed -n '2,20p' "$0" | sed 's/^# \?//'; exit 0 ;;
		-*) echo "announce.sh: unknown argument '$arg' (see --help)" >&2; exit 2 ;;
		*)  version="${arg#v}" ;;
	esac
done

[ -n "$version" ] || { echo "announce.sh: pass the released version, e.g. scripts/announce.sh 0.1.1" >&2; exit 2; }

# ⚠ EVERY FAILURE PAST THIS POINT IS A WARNING AND A NON-ZERO RETURN, NEVER AN ABORT MID-WRITE. The
# release is already public; the worst outcome here is an announcement that has to be written by hand.
warn() { echo "announce.sh: $*" >&2; }

body="$(scripts/changelog.sh --section="$version")" || {
	warn "CHANGELOG.md has no section for $version — regenerate it before announcing"
	exit 1
}

post_markdown() {
	cat <<EOF
---
title: Maxon $version
description: Release notes for Maxon $version — what changed in the compiler and standard library.
date: $(date +%Y-%m-%d)
authors: maxon
tags:
  - release
excerpt: Maxon $version is out. Here's what changed.
---

**Maxon $version is released.** Archives for every supported target are on the
[GitHub releases page]($ReleasesUrl/tag/v$version), alongside a Windows installer.

## What changed

$body

## Download

\`\`\`
winget install MaxonLang.Maxon          # Windows
brew install maxon-lang/tap/maxon       # macOS
\`\`\`

Elsewhere, take the archive for your platform from the
[releases page]($ReleasesUrl/tag/v$version) and follow the \`INSTALL.md\` inside it.

⚠ Each archive holds the \`maxon\` compiler and \`stdlib/\` **as siblings**, and that layout is the
contract: the compiler finds its standard library by walking up from its own executable, so moving the
binary out on its own leaves it without one.
EOF
}

# ⭐ THE DATE IS TODAY'S HERE, DELIBERATELY, AND UNLIKE EVERYWHERE ELSE IN THIS TOOLING. A changelog
# entry is dated by the history it describes; a blog post is dated by the day it was published, which
# is what a reader of a feed expects.

# ⭐⭐ **THE SITE'S CHANGELOG PAGE — EVERY RELEASE, REWRITTEN WHOLE EACH TIME.** The blog post answers
# *"what changed in 0.1.1"*; this answers *"has the thing I need landed yet"*, which is the question a
# reader arrives with when they are deciding whether to upgrade. Both are renderings of the same
# `CHANGELOG.md`, so neither can drift from what shipped.
#
# ⚠ THE SOURCE FILE'S OWN HEADER IS DROPPED AND REPLACED. `CHANGELOG.md` opens with a note to whoever
# writes the next entry — where the reference listing is, where the release order is written down —
# which is addressed to a contributor and means nothing to a reader of the website.
changelog_page() {
	cat <<EOF
---
title: Changelog
description: Every released version of the Maxon compiler and standard library, and what changed in it.
---

Every released version, newest first.

Downloads for each release are on the
[GitHub releases page]($ReleasesUrl), and the install instructions are in
[Installation](/docs/getting-started/installation/).

EOF
	# From the first version heading to the end: the headings and bullets, without the contributor
	# preamble above them.
	sed -n '/^## /,$p' "$ChangelogFile"
}

if [ "$dry_run" -eq 1 ]; then
	post_markdown
	echo
	echo "───────────────────────── $ChangelogPagePath ─────────────────────────"
	echo
	changelog_page
	exit 0
fi

[ -d "$web_dir/.git" ] || { warn "'$web_dir' is not a git checkout; nothing announced"; exit 1; }

blog_dir="$web_dir/$BlogSubdir"
[ -d "$blog_dir" ] || { warn "'$blog_dir' does not exist; nothing announced"; exit 1; }

# ⛔ **PUSHING TO WHATEVER BRANCH HAPPENED TO BE CHECKED OUT IS HOW AN ANNOUNCEMENT NEVER DEPLOYS.**
# Cloudflare builds `main`; a release announced onto somebody's half-finished feature branch is
# committed, pushed, reported as done, and invisible. MEASURED: the checkout this was written against
# was sitting on `upgrade-astro-7`.
web_branch="$(git -C "$web_dir" branch --show-current)"
if [ "$web_branch" != "$WebDefaultBranch" ]; then
	warn "'$web_dir' is on '$web_branch', not '$WebDefaultBranch' — the site deploys from '$WebDefaultBranch'"
	warn "nothing announced. Switch that checkout and run this again."
	exit 1
fi

# Slug `maxon-0-1-1`, so the URL is /blog/maxon-0-1-1.
post="$blog_dir/maxon-${version//./-}.md"

# ⛔ NEVER OVERWRITE. A post that already exists may have been edited by hand after publication, and
# regenerating over it would silently discard that.
[ ! -e "$post" ] || { warn "$post already exists; not overwriting"; exit 1; }

page="$web_dir/$ChangelogPagePath"
[ -d "$(dirname "$page")" ] || { warn "'$(dirname "$page")' does not exist; nothing announced"; exit 1; }

# ⛔ **THE SITE NAMES THE RELEASED VERSION IN ITS DOWNLOAD LINKS, and a stale one is a 404 rather
# than a cosmetic slip.** The asset filenames carry the version, so a release that updated the blog
# and not this leaves every download button pointing at the PREVIOUS release's files. The site keeps
# it in one module for exactly this reason; the docs pages name no version at all.
version_module="$web_dir/$VersionModulePath"
[ -f "$version_module" ] || { warn "'$version_module' does not exist; nothing announced"; exit 1; }
grep -q "RELEASE_VERSION = '" "$version_module" || {
	warn "'$version_module' does not declare RELEASE_VERSION in the expected shape; nothing announced"
	exit 1
}

post_markdown > "$post"
echo "announce.sh: wrote $post"

# ⚠ THE PAGE IS REWRITTEN, NOT APPENDED TO — unlike the post, which is refused if it exists. It is
# generated in full from `CHANGELOG.md` every time, so there is nothing hand-written in it to lose.
changelog_page > "$page"
echo "announce.sh: wrote $page"

sed -i "s/RELEASE_VERSION = '[^']*'/RELEASE_VERSION = '$version'/" "$version_module"
echo "announce.sh: set RELEASE_VERSION to $version in $version_module"

# ⚠ ONLY THESE TWO FILES ARE STAGED, so an unrelated change sitting in that repository's working tree
# is never swept into this commit.
git -C "$web_dir" add "$post" "$page" "$version_module" || { warn "could not stage the release's website changes"; exit 1; }
git -C "$web_dir" commit -q -m "release: Maxon $version — announcement, changelog and download links" || {
	warn "could not commit in $web_dir"
	exit 1
}

echo "announce.sh: pushing to maxon.dev ($web_branch) — Cloudflare deploys on push"
git -C "$web_dir" push origin "$web_branch" || {
	warn "committed the post but could not push it"
	warn "push it by hand: git -C '$web_dir' push origin $web_branch"
	exit 1
}

echo "announce.sh: announced Maxon $version"
