# maxon-web

The website for the [Maxon programming language](https://maxon.dev) — a marketing landing
page plus full documentation, examples, and install instructions.

Built with **Astro** + **Starlight** + **Tailwind**, output as a static site. Maxon code is
syntax-highlighted at build time using the language's own TextMate grammar (the same one the
VS Code extension ships), so highlighting always matches the editor.

## Local development

```bash
npm install
npm run dev        # dev server at http://localhost:4321
npm run build      # static build to ./dist
npm run preview    # serve the built site locally
```

Requires Node.js 22.12+ (Astro 7's minimum).

## Project structure

```
src/
  pages/                 Bespoke marketing pages (Astro)
    index.astro          Landing — "Written by AI, for AI"
    examples.astro       Curated, highlighted example programs
  layouts/
    MarketingLayout.astro  Shared shell (nav/footer) for the marketing pages
  components/
    MaxonCode.astro      Maxon-highlighted code block for marketing pages
    PlatformTabs.astro   Per-OS command block; shares its selection with the docs
    CommandBlock.astro   Commands with a copy button, inside PlatformTabs
    starlight/           Starlight component overrides (the docs' dark default)
  lib/
    og-card.ts           Draws the per-page Open Graph card (SVG → PNG via sharp)
  route-data.ts          Starlight route middleware — points each page at its OG card
  content/docs/docs/     Starlight docs collection (served under /docs/*)
    getting-started/     Hand-authored intro / install / first-program
    language/  stdlib/   Generated from ../docs/ by scripts/sync-docs.mjs — never hand-edited
    cli/  best-practices/  spec/   Generated likewise (cli/error-codes.md from the error-code registry)
    blog/                Blog posts (starlight-blog plugin) — served at /blog/
  grammars/
    maxon.tmLanguage.json  Maxon TextMate grammar (copied from the compiler repo)
  examples/              Real .maxon programs (copied from the compiler repo)
  assets/
  styles/
    global.css           Tailwind entry + theme (marketing pages)
    theme.css            Starlight brand overrides (docs)
public/                  favicon, og image
  pages/og/[...slug].png.ts  Build-time OG card per docs page
astro.config.mjs         Site config, sidebar, plugins, Shiki/Expressive-Code grammar registration
```

The docs live at `src/content/docs/docs/` (nested one level) so Starlight serves them under
`/docs/…` rather than the site root.

## Content sourcing

The reference pages — `cli/`, `language/`, `stdlib/`, `spec/` and `best-practices/` under
`src/content/docs/docs/`, including the Error Codes page — are **generated** from the repository's
`docs/*.md` and `maxon-bin/Compiler/ErrorCodeRegistry.maxon` by `node scripts/sync-docs.mjs`, and CI fails
when they drift. Never edit a generated page's body: edit its source and re-run the sync. Only each
page's front matter is written here.

The syntax grammar (`src/grammars/maxon.tmLanguage.json`) and the example programs (`src/examples/`) are
still copied by hand, and the `getting-started/`, `contributing`, `about` and `changelog` pages are
written for this site.

[MAINTAINING.md](MAINTAINING.md#content-sync--the-reference-pages-are-generated) has the source map (what
changed → which file to edit), the sync's rules, and the link forms a source uses.

## Blog

The blog uses the [`starlight-blog`](https://github.com/HiDeoo/starlight-blog) plugin
(pinned to `0.16.x` for Starlight 0.30 compatibility). It's a mixed feed — release notes and
essays — served at `/blog/`, with an RSS feed at `/blog/rss.xml`, plus tag and author pages.

To add a post, drop a Markdown file in `src/content/docs/blog/`:

```markdown
---
title: Maxon v1.1
description: One-line summary for SEO and the post header.
date: 2026-06-15            # YYYY-MM-DD; controls ordering
authors: maxon             # author key defined in astro.config.mjs
tags:
  - release                # free-form; tag pages are generated automatically
excerpt: Shown on the blog index and in social/RSS previews.
---

Post body in Markdown. Maxon code fences (```maxon) highlight automatically.
```

Authors are defined once in `astro.config.mjs` under the `starlightBlog({ authors })` option.

## Brand

- Accent: Maxon cyan `#00ADD8` — the Tailwind `brand` scale, defined in the `@theme` block of
  `src/styles/global.css`, and the Starlight accent in `src/styles/theme.css`.
  Tailwind v4 is configured in CSS; there is no `tailwind.config.mjs`.
- The logo/favicon reuse the diamond-"M" mark from the VS Code extension.
- Marketing pages are dark, terminal-inspired; the docs start dark and follow the light/dark toggle.

## Deployment

Static output in `./dist`, deployed to **Cloudflare Pages** (project `maxon-dev`) by
`.github/workflows/website.yml` when a release is published — the site describes the released
compiler, so it ships when the compiler does. `workflow_dispatch` deploys on demand between
releases.

See [MAINTAINING.md](MAINTAINING.md) for the full picture, including why Cloudflare's Pages git
integration must stay disconnected.


## License

Maxon is dual-licensed under MIT and Apache-2.0.
