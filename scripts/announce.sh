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
# ⭐ **THE CHANGES COME FROM `CHANGELOG.md`, THROUGH `changelog.sh --section`.** The post, the GitHub
# release notes and the marketplace tab are then three renderings of one source, and cannot come to
# disagree about what shipped.
#
# Usage:
#   scripts/announce.sh <version> [--web-dir=<path>] [--dry-run]
#
#   <version>     the released version, without a leading `v` — e.g. 0.1.1
#   --web-dir=    the maxon-web checkout. Defaults to $MAXON_WEB_DIR, else ../maxon-web
#   --dry-run     print the post to stdout, write and push nothing

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"

ReleasesUrl="https://github.com/maxon-lang/maxon/releases"
BlogSubdir="src/content/docs/blog"

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

if [ "$dry_run" -eq 1 ]; then
	post_markdown
	exit 0
fi

[ -d "$web_dir/.git" ] || { warn "'$web_dir' is not a git checkout; nothing announced"; exit 1; }

blog_dir="$web_dir/$BlogSubdir"
[ -d "$blog_dir" ] || { warn "'$blog_dir' does not exist; nothing announced"; exit 1; }

# Slug `maxon-0-1-1`, so the URL is /blog/maxon-0-1-1.
post="$blog_dir/maxon-${version//./-}.md"

# ⛔ NEVER OVERWRITE. A post that already exists may have been edited by hand after publication, and
# regenerating over it would silently discard that.
[ ! -e "$post" ] || { warn "$post already exists; not overwriting"; exit 1; }

post_markdown > "$post"
echo "announce.sh: wrote $post"

# ⚠ ONLY THE NEW POST IS STAGED, so an unrelated change sitting in that repository's working tree is
# never swept into this commit.
git -C "$web_dir" add "$post" || { warn "could not stage $post"; exit 1; }
git -C "$web_dir" commit -q -m "blog: announce Maxon $version" || { warn "could not commit $post"; exit 1; }

web_branch="$(git -C "$web_dir" branch --show-current)"
echo "announce.sh: pushing to maxon.dev ($web_branch) — Cloudflare deploys on push"
git -C "$web_dir" push origin "$web_branch" || {
	warn "committed the post but could not push it"
	warn "push it by hand: git -C '$web_dir' push origin $web_branch"
	exit 1
}

echo "announce.sh: announced Maxon $version"
