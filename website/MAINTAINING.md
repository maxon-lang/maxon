# Maintaining maxon.dev

Operational notes for the Maxon website. It lives in the compiler repository, at `website/`, and
this captures the things that aren't obvious from the code — how it deploys, where content comes
from, and the positioning decisions baked into the copy. The [README](README.md) covers local dev and the
project layout; this doc covers running and evolving the site.

## How it's hosted and deployed

- **Host:** Cloudflare Pages, project `maxon-dev`.
- **Deploy = a published release.** `.github/workflows/website.yml` builds this directory and
  deploys it with `wrangler` when `release.yml`'s `publish` job starts it — once every asset is
  built, tested and uploaded. ⭐ **The site describes the released compiler**, so it ships when the
  compiler does: its download links carry the version and its docs teach one build's syntax, and
  deploying `main` continuously would publish instructions for something nobody can download yet.
- **Between releases, nothing deploys automatically.** Run the workflow by hand
  (`workflow_dispatch`) for an essay or a typo you don't want to sit on.
- **Every change here is still built on push and on pull requests**, without deploying —
  `starlight-links-validator` fails the build on a dead internal link, so a broken site is a red
  pull request rather than a broken deploy.
- ⛔ **The Pages project keeps its git connection, with automatic deployments DISABLED on all
  branches.** A Pages project cannot be converted from Git integration to Direct Upload — Cloudflare:
  *"If you deploy using the Git integration, you cannot switch to Direct Upload later."* Disabling
  branch control is the supported way to stop it building while `wrangler` deploys directly. The
  connection points at the archived `maxon-lang/maxon.dev`, so re-enabling it would deploy a repository
  nobody pushes to.
- **Domain/DNS:** `maxon.dev` registered at Namecheap, **nameservers delegated to Cloudflare**.
  Cloudflare manages the apex + `www` records and TLS automatically. The website is the only
  thing on `maxon.dev`.
- **To verify a deploy:** watch the `website` workflow run, then load https://maxon.dev. The
  Cloudflare Pages dashboard shows what was uploaded.
- ⛔ **`public/install.sh` and `public/install.ps1` are the installers, and a deploy publishes them.**
  They are what `curl … | sh` and `irm … | iex` fetch, and what `maxon upgrade` runs, so a deploy
  with a broken one breaks installing and upgrading for everyone. `install-script.yml` tests every
  change to them; deploy only a commit where it is green. `public/_headers` serves both as
  `text/plain`, so "Read the script" shows them in the browser rather than downloading them.
- **Build output:** static HTML pages plus a Pagefind search index and
  `sitemap-index.xml`. `site: 'https://maxon.dev'` is set in `astro.config.mjs` — keep it
  accurate or canonical URLs / sitemap / OG links break.

## Tech stack quick reference

- **Astro** (static output) + **Starlight** (the `/docs` section) + **Tailwind** (the bespoke
  marketing pages). See [README](README.md) for the file layout.
- **Versions travel together.** `astro`, `@astrojs/starlight` and `starlight-blog` are a single
  locked set — Starlight pins an exact Astro range and `starlight-blog` pins a Starlight range,
  so bumping one alone fails to install. Upgrade all three in one step and re-verify.
- **Tailwind is v4**, wired in as a Vite plugin (`@tailwindcss/vite`) rather than an Astro
  integration — `@astrojs/tailwind` was discontinued after Astro 5. The theme (brand palette,
  fonts) lives in the `@theme` block of `src/styles/global.css`; there is no JS config file.
- **Whitespace in markup is JSX-flavoured.** Astro 7 defaults to `compressHTML: 'jsx'`, which
  drops the newline between an inline element and the text on the next line. Prose that wraps
  across such a boundary needs an explicit `{' '}` or the words render glued together.
- **Starlight plugins:** `starlight-blog` (the `/blog` feed), `starlight-llms-txt` (publishes
  `/llms.txt`, `/llms-small.txt` and `/llms-full.txt`), and `starlight-links-validator`.
- **The build fails on a broken internal link.** That is the point of it — fix the link rather
  than reaching for the plugin's `exclude`. `exclude` is only for routes the validator genuinely
  cannot see, which here means the bespoke marketing pages (`/examples/`, `/blog/`);
  they are real routes but they are not Starlight's, so it reports them as dead ends.
- **`starlight-changelogs` is not usable yet.** Its `@ascorbic/loader-utils` dependency still
  peers on Astro 4/5, so npm satisfies it by installing a second, older Astro alongside ours —
  which is where five audit advisories came from. Re-check when that dependency supports Astro 7.
- **Open Graph cards are generated per docs page** by `src/pages/og/[...slug].png.ts`, drawn in
  `src/lib/og-card.ts` and attached by the route middleware in `src/route-data.ts`. Starlight
  emits `twitter:card: summary_large_image` but no `og:image` of its own, so without this every
  shared docs link previews blank. Pages with no content entry (the blog index, tag and author
  listings, 404) fall back to the site-wide `public/og.png`.
- **There is one installation page, `/docs/getting-started/installation/`.** Link to it for anything
  about installing; the marketing pages have no install page of their own.
- **Platform tabs are shared between the docs and the home page.** Starlight's
  `<Tabs syncKey="os">` persists the chosen tab's *label* under `starlight-synced-tabs__os`, and
  `PlatformTabs.astro` reads and writes that same key. **The labels must match exactly** —
  `macOS & Linux`, `Windows` — in `installation.mdx` and `quickstarts` in `index.astro`. Change one
  and the sync silently stops working. A tab's
  `platforms` lists the detected platforms it is preselected for, so one tab can serve two.
- **The docs start in dark mode**, to match the marketing pages, which are dark only. Starlight would
  follow the visitor's system scheme; `src/components/starlight/ThemeProvider.astro` stores `dark` as
  the choice of a visitor who has never made one, before Starlight's own provider reads it. The picker
  still offers light and auto, and a choice made there is kept.
- **Every command shown has a copy button.** On the marketing pages that is `CommandBlock.astro`,
  which `PlatformTabs` renders through: pass commands as `lines` (a `#` line is a
  comment, an empty one a gap) and the button copies exactly the commands, never the prompt, the
  comments or the output. Don't hand-write a command in a `<pre>`. In the docs, Expressive Code puts a
  button on every fenced block and on `<Code>`; a command worth copying belongs in one, not inline in
  prose. zsh does not treat `#` as a comment at an interactive prompt, so a block a reader is meant to
  paste and run (installing, building from source) holds commands only; explain them in prose.
- **Syntax highlighting:** the Maxon TextMate grammar (`src/grammars/maxon.tmLanguage.json`) is
  copied from the compiler repo's VS Code extension and registered with Shiki / Expressive
  Code. ` ```maxon ` fenced blocks highlight at build time, matching the editor exactly.

## Content sync — the reference pages are generated

⛔ **The reference pages are GENERATED. Never edit a generated page's body** — edit its source and
re-run the sync. `node website/scripts/sync-docs.mjs` builds every page under `cli/`, `language/`,
`stdlib/`, `spec/` and `best-practices/` from `docs/*.md` and from the error-code registry:

- `docs/CLI_REFERENCE.md`, `docs/LANGUAGE_REFERENCE.md` and `docs/STDLIB_REFERENCE.md` are **split at
  their `## ` headings** by the section map (`SOURCES`) at the top of the script, several pages each.
- `docs/BNF_SYNTAX.md`, `docs/WRITING_MAXON_CODE.md` and `docs/BEST_PRACTICES.md` are one page each.
- `cli/error-codes.md` is built from the `//` doc comment above each case in
  `maxon-bin/Compiler/ErrorCodeRegistry.maxon`, grouped under the stage names `lookup_error_code` reports
  (`stageForCode` in `maxon-bin/Compiler/Mcp/McpErrorCodes.maxon`).

A page's front matter (`title`, `description`, `sidebar.order`) is the one part written by hand, and the
sync never touches it. **Quote any `description` containing a colon** — an unquoted `key: value` colon is
a YAML parse error that fails the whole build.

**CI fails on drift.** The `website` workflow runs `sync-docs.mjs --check` on every push and pull request
that touches `docs/`, the registry or the site, so a source that moved on without its pages is a red
build. Run `node website/scripts/sync-docs.mjs` and commit the regenerated pages with the source change.

**The sync fails closed** and writes nothing while any problem stands, naming each one:

- a `## ` heading in a split source that no page claims and `notPublished` does not list, a heading two
  pages claim, or a listed heading the source does not have — **renaming, adding or removing a `## `
  section means updating `SOURCES`**, and a new page needs its file created with front matter first;
- a link to an anchor no heading produces, into a section that is not published, to a relative path
  with no route, to an error code the registry does not declare, or to a `/docs/…` route of a generated
  page;
- a registry case with no doc comment, a comment separated from its case by a blank line, a duplicate
  number, or a line that is not a comment, a blank or a case;
- a file under a generated directory that no source produces.

**Links in a source are written for a reader of the repository**, and the sync turns each into the route
of the page that holds its target:

| Link to | Write |
| --- | --- |
| A heading in the same file | `[text](#anchor)` |
| A heading in another synced file | `[text](CLI_REFERENCE.md#anchor)` |
| An error code on the Error Codes page | `[E3014](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3014)` (the code in lower case; no anchor links the page) |
| A page written for the site alone (`/docs/getting-started/…`, `/docs/contributing/`, `/docs/about/`, `/docs/changelog/`, `/examples/`, `/blog/`) | its route, e.g. `[Contributing](/docs/contributing/)` |

⛔ A route to a generated page (`/docs/cli/…`, `/docs/language/…`, `/docs/stdlib/…`, `/docs/spec/…`,
`/docs/best-practices/…`) is refused in a source: it is a dead link in the repository, and the heading it
names is not checked until the site builds.

### What changed → which source to edit

This is the one map of where the site's documentation comes from.

| What changed | Edit |
| --- | --- |
| A CLI command, option or `maxon help` text | `docs/CLI_REFERENCE.md` (Commands) |
| A diagnostic | its doc comment in `maxon-bin/Compiler/ErrorCodeRegistry.maxon` — the Error Codes page regenerates |
| Syntax or semantics | `docs/LANGUAGE_REFERENCE.md`, and `docs/BNF_SYNTAX.md` for the grammar |
| A `public` standard-library API | `docs/STDLIB_REFERENCE.md` |
| A runtime environment variable | `docs/CLI_REFERENCE.md`, Commands › Environment Variables |
| Target support | `docs/CLI_REFERENCE.md`, Targets |
| Language server or VS Code behaviour | `docs/CLI_REFERENCE.md`, Editor Support, and `vscode-extension/README.md` |
| An MCP tool or its arguments | `docs/CLI_REFERENCE.md`, MCP Server |
| The install scripts | `public/install.sh` / `public/install.ps1`, and the pages listed under [Install and build instructions](#install-and-build-instructions-follow-the-scripts-and-the-repository) |
| The syntax grammar | `vscode-extension/syntaxes/maxon.tmLanguage.json`, then copy it to `src/grammars/maxon.tmLanguage.json` **by hand** |
| An example program | `examples/*.maxon`, then copy it to `src/examples/` **by hand** |

The grammar and the example programs are still manual copies: nothing checks them against their
sources. The `getting-started/`, `contributing`, `about` and `changelog` pages are written for this site
and have no upstream source.

**The doc-coverage gates catch what a source is missing**, and run with `maxon test`, not with the site:

- `tests/cli/reference-documents-every-command.maxtest` — every command and option `maxon help` lists
  is in `docs/CLI_REFERENCE.md`;
- `tests/cli/reference-documents-only-real-options.maxtest` — every option that document shows is one
  the driver has;
- `tests/mcp/reference-documents-every-tool.maxtest` — its MCP Server section names every tool the
  server advertises and every argument each declares;
- `tests/docs/stdlib-reference-documents-every-public-api.maxtest` — `docs/STDLIB_REFERENCE.md` names
  every `public` declaration in `stdlib/`.

When writing a source:

- Tag Maxon code fences ` ```maxon ` so they highlight; leave shell fences ` ```bash `.
- Keep the agent-facing "how to write Maxon" framing — it's intentional and on-brand. Sections that
  describe this repository rather than the language belong in `notPublished`.

## Install and build instructions follow the scripts and the repository

The install commands and paths on this site are the ones `public/install.sh` and
`public/install.ps1` implement: `~/.maxon/bin` and `~/.maxon/stdlib`, `MAXON_INSTALL`, and the
options each script's usage lists. The build-from-source steps are the repository's own
(`README.md`, `CONTRIBUTING.md`): seed `.bootstrap/` with `scripts/fetch-seed.sh`, then
`scripts/build-from-seed.sh`.

When either changes, the pages to update are `src/install.ts` (the install commands, which the home
page and `installation.mdx` both render from), `getting-started/installation.mdx`,
`getting-started/first-program.md`, `contributing.md`, and the quickstart in `index.astro`.

⛔ **Release posts and GitHub release notes link to the installation page and carry no install
commands.** Both are published once and never revised, and the commands change between releases.
`scripts/announce.sh` and `scripts/release.sh` write them that way.

⚠ **The compiler prints the install commands too.** `maxon upgrade` gives this host's one-liner when it
refuses an install it does not manage, from `maxon-bin/Upgrade/UpgradeCommand.maxon`. Change it on the
site and change it there as well. `tests/cli/upgrade-refuses-an-unrecognised-layout.maxtest` reads the
one-liner out of `src/install.ts` and fails until the two agree.

## Positioning & copy decisions (keep these consistent)

These are deliberate and easy to undo by accident — preserve them:

- **Motto:** **"*You* Aren't Going To Write It."** (single line), everywhere — hero, page
  `<title>`, OG image, blog post and docs intro blockquotes. **The "You" carries a light
  emphasis** — italic and `text-brand` in the hero and the OG image, italic inside the bold in
  Markdown (`***You* aren't going to write it.**`). It is the contrast the line turns on; a
  plain-weight "You" loses the joke. Plain text where markup is impossible (`<title>`, the blog
  frontmatter `title`). ⛔ **The site does not claim the
  reader will read the code.** No "...You Are Going To Read It.", no "optimized for the
  *reader*", no "designed to be read": the promise is that the code *is reviewable when you
  check it*, not that you will be reading it.
- **Core thesis:** written by AI, for AI — the AI writes the code, so the code has to answer
  for itself, and the language optimizes for *review, not keystrokes*. Whatever a reviewer
  goes looking for is on the page rather than reconstructed.
  Verbosity/explicitness is the product, not a cost. This is the answer to "but it's less
  concise."
- **Maturity:** the project is **early** (pre-1.0, self-hosting in progress). The site must
  **not** imply production-readiness:
  - Version is **v0.1**; the header badge reads **"v0.1 · early preview"**.
  - An **"early preview / breaking changes expected before 1.0"** banner shows on every page.
    On marketing pages it's in `MarketingLayout.astro`; on docs it's a **default `banner`** set
    in `src/content.config.ts` (Starlight's `banner` is per-page frontmatter, so it's defaulted
    in the content schema, not in `astro.config.mjs`).
  - Avoid unverified claims like "fast" or "production". The feature section says
    "**A real language underneath**" / "early, but real" — not "serious".
- **Don't publish invented stats.** No "built in N days" / "N commits" figures. An early
  exploration guessed "~18 days / 224 commits" and it was wrong. The About page's timeline is
  qualitative and traced to real git history (C++ → Zig → C# bootstrap compilers →
  self-hosted); keep it that way.
- **"Free and open source"** is a featured selling point (first feature card + footer). Note
  the repo only became public as part of the v1 release push — keep GitHub/clone links pointed
  at `https://github.com/maxon-lang/maxon`.

## Generated assets

- **OG image** (`public/og.png`) is generated, not hand-edited. Edit `scripts/make-og.mjs` and
  run `node scripts/make-og.mjs` to regenerate, then commit the PNG. It carries the motto and
  must stay in sync with the hero copy.
- **Favicon / logo** reuse the diamond-"M" mark from the compiler's VS Code extension.

## Gotchas worth remembering

- **Editor errors from `node_modules`:** if VS Code's Problems panel shows TypeScript errors
  inside dependencies, that's the TS server walking `node_modules`. `tsconfig.json` already
  sets `skipLibCheck` and scopes `include` to `src/**`. The real check is `npx astro check`
  (and `npm run build`), not the editor panel.
- **Local preview + the Docker browser:** the Playwright MCP browser runs in a container and
  can't reach host `localhost`. To screenshot a local preview, run
  `npx astro preview --host 0.0.0.0` and navigate via the host's LAN IP, not `localhost`.
  Simpler: just open `localhost:4321` in your own browser.
- **Always `npm run build` before pushing.** A push only builds, but a red build on `main` is a
  release that cannot deploy: the site ships from the release tag (or a manual `workflow_dispatch`), and
  it ships whatever builds there. `npx astro check` should report 0 errors too.
