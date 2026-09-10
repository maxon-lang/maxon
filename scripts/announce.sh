#!/usr/bin/env bash
#
# Write the website's release material: the announcement post, the changelog page, and the version
# the download links are built from.
#
# ⭐⭐ **IT WRITES FILES AND STOPS. IT DOES NOT COMMIT, AND IT DOES NOT PUSH.** This runs while a
# `release/X.Y.Z` branch is being prepared, so its output is committed with everything else that
# branch carries and reaches the site when the tag is published — `website.yml` deploys on
# `release: published`, building from the tag. There is nothing here to get out of step with the
# release, because it is part of it.
#
# ⭐ **THE CHANGES COME FROM `CHANGELOG.md`.** The post, the site's changelog page and the GitHub
# release notes are three renderings of one hand-written source and cannot come to disagree about
# what shipped.
#
# ⚠ **THREE FILES, THREE DIFFERENT JOBS.** The post announces THIS release and is never touched
# again; the changelog page carries EVERY release and is rewritten each time; `version.ts` is what
# the download links are built from, so skipping it would leave every download button pointing at the
# previous release's filenames.
#
# Usage:
#   scripts/announce.sh <version> [--dry-run]
#
#   <version>     the version being released, without a leading `v` — e.g. 0.1.1
#   --dry-run     print all three to stdout and write nothing

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"

ReleasesUrl="https://github.com/maxon-lang/maxon/releases"
WebsiteDir="website"
BlogSubdir="$WebsiteDir/src/content/docs/blog"
ChangelogPagePath="$WebsiteDir/src/content/docs/docs/changelog.md"
VersionModulePath="$WebsiteDir/src/version.ts"
ChangelogFile="CHANGELOG.md"

version=""
dry_run=0

for arg in "$@"; do
	case "$arg" in
		--dry-run)   dry_run=1 ;;
		--help|-h)   sed -n '2,26p' "$0" | sed 's/^# \?//'; exit 0 ;;
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
# ⚠ THE SOURCE FILE'S OWN HEADER IS DROPPED AND REPLACED, so the page can say where the downloads and
# the install instructions are — which the changelog itself deliberately never does.
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
[ -d "$BlogSubdir" ] || { warn "'$BlogSubdir' does not exist"; exit 1; }
[ -d "$(dirname "$ChangelogPagePath")" ] || { warn "'$(dirname "$ChangelogPagePath")' does not exist"; exit 1; }

# ⛔ **THE SITE NAMES THE RELEASED VERSION IN ITS DOWNLOAD LINKS, and a stale one is a 404 rather than
# a cosmetic slip.** The asset filenames carry the version, so a release that updated the changelog and
# not this leaves every download button pointing at the previous release's files. The site keeps it in
# one module for exactly this reason; the docs pages name no version at all.
[ -f "$VersionModulePath" ] || { warn "'$VersionModulePath' does not exist"; exit 1; }
grep -q "RELEASE_VERSION = '" "$VersionModulePath" || {
	warn "'$VersionModulePath' does not declare RELEASE_VERSION in the expected shape"
	exit 1
}

# Slug `maxon-0-1-1`, so the URL is /blog/maxon-0-1-1.
post="$BlogSubdir/maxon-${version//./-}.md"

# ⛔ NEVER OVERWRITE. A post that already exists may have been edited by hand after publication, and
# regenerating over it would silently discard that.
[ ! -e "$post" ] || { warn "$post already exists; not overwriting"; exit 1; }

post_markdown > "$post"
echo "announce.sh: wrote $post"

# ⚠ THE PAGE IS REWRITTEN, NOT APPENDED TO — unlike the post, which is refused if it exists. It is
# generated in full from `CHANGELOG.md` every time, so there is nothing hand-written in it to lose.
changelog_page > "$ChangelogPagePath"
echo "announce.sh: wrote $ChangelogPagePath"

sed -i "s/RELEASE_VERSION = '[^']*'/RELEASE_VERSION = '$version'/" "$VersionModulePath"
echo "announce.sh: set RELEASE_VERSION to $version in $VersionModulePath"

echo ""
echo "announce.sh: three files written and nothing committed. Commit them on the release branch:"
echo "  git add $WebsiteDir && git commit -m 'website: Maxon $version'"
