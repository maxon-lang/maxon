# Maintaining maxon.dev

Operational notes for the Maxon website. It lives in the compiler repository, at `website/`, and
this captures the things that aren't obvious from the code — how it deploys, where content comes
from, and the positioning decisions baked into the copy. The [README](README.md) covers local dev and the
project layout; this doc covers running and evolving the site.

## How it's hosted and deployed

- **Host:** Cloudflare Pages, project `maxon-dev`.
- **Deploy = a published release.** `.github/workflows/website.yml` builds this directory and
  deploys it with `wrangler` on `release: published` — once `release.yml` has built, tested and
  uploaded every asset. ⭐ **The site describes the released compiler**, so it ships when the
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
- **Build output:** static, 34 pages as of this writing, plus a Pagefind search index and
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
  cannot see, which here means the bespoke marketing pages (`/install/`, `/examples/`, `/blog/`);
  they are real routes but they are not Starlight's, so it reports them as dead ends.
- **`starlight-changelogs` is not usable yet.** Its `@ascorbic/loader-utils` dependency still
  peers on Astro 4/5, so npm satisfies it by installing a second, older Astro alongside ours —
  which is where five audit advisories came from. Re-check when that dependency supports Astro 7.
- **Open Graph cards are generated per docs page** by `src/pages/og/[...slug].png.ts`, drawn in
  `src/lib/og-card.ts` and attached by the route middleware in `src/route-data.ts`. Starlight
  emits `twitter:card: summary_large_image` but no `og:image` of its own, so without this every
  shared docs link previews blank. Pages with no content entry (the blog index, tag and author
  listings, 404) fall back to the site-wide `public/og.png`.
- **Platform tabs are shared between the docs and the marketing pages.** Starlight's
  `<Tabs syncKey="os">` persists the chosen tab's *label* under `starlight-synced-tabs__os`, and
  `PlatformTabs.astro` reads and writes that same key. **The labels must match exactly** —
  `Windows`, `macOS`, `Linux` — in `installation.mdx`, `installCommands` in `install.astro`, and
  `quickstarts` in `index.astro`. Change one and the sync silently stops working.
- **Syntax highlighting:** the Maxon TextMate grammar (`src/grammars/maxon.tmLanguage.json`) is
  copied from the compiler repo's VS Code extension and registered with Shiki / Expressive
  Code. ` ```maxon ` fenced blocks highlight at build time, matching the editor exactly.

## Content sync (IMPORTANT — manual, not automatic)

The docs, the grammar, and the example programs are **copied** from the compiler repo
(`../docs/`) and curated for a public audience. **Nothing is auto-synced.** When the language
or its docs change, refresh these deliberately:

| This directory | Source in the repository root |
| --- | --- |
| `src/grammars/maxon.tmLanguage.json` | `vscode-extension/syntaxes/maxon.tmLanguage.json` |
| `src/examples/*.maxon` | `examples/*.maxon` |
| `src/content/docs/docs/language/*` | `docs/LANGUAGE_REFERENCE.md` (split by section) |
| `src/content/docs/docs/stdlib/` | `docs/STDLIB_REFERENCE.md` |
| `src/content/docs/docs/cli/` | `docs/CLI_REFERENCE.md` |
| `src/content/docs/docs/best-practices/*` | `docs/WRITING_MAXON_CODE.md`, `docs/BEST_PRACTICES.md` |
| `src/content/docs/docs/spec/` | `docs/BNF_SYNTAX.md` |

The `getting-started/`, `contributing`, and `about` pages are written for this site and have
no single upstream source. **Best practice:** re-sync from a *tagged* compiler release so the
published docs match shipped behavior, not in-progress work.

When importing curated Markdown:

- Add Starlight front matter (`title`, `description`, `sidebar.order`). **Quote any
  `description` containing a colon** — an unquoted `key: value` colon is a YAML parse error
  that fails the whole build. (This bit us during the initial import.)
- Tag Maxon code fences ` ```maxon ` so they highlight; leave shell fences ` ```bash `.
- Trim compiler-repo-internal notes, but **keep** the agent-facing "how to write Maxon"
  framing — it's intentional and on-brand.
- Files copied from the Windows compiler repo may have **CRLF** line endings; be careful with
  scripted edits to YAML front matter (a `\r` can swallow the line break).

## The build is the source of truth, not the upstream README

The compiler's build setup changed over time and the upstream README lagged reality. The
website's install instructions were corrected against the **actual** build, not the README:

- The shipping compiler is the **C# one** (`maxon-sharp`, targets **.NET 10**), which builds
  to `bin/maxon`. There is **no C++ compiler, no `make`, no CMake/Ninja** anymore (those were
  removed; the README was stale).
- Build: `dotnet build maxon-sharp`. On Windows, `buildall.bat` builds + tests the full
  toolchain (C# compiler, self-hosted compiler, spec tests).
- Run a program: `bin/maxon build <file>`. Run tests: `bin/maxon spec-test`.
- There is **no verified Linux/macOS build script** — `buildall.bat` is Windows-only. The
  install page deliberately doesn't promise a cross-platform build path it can't back up. If
  one is added upstream, update `src/pages/install.astro` and
  `src/content/docs/docs/getting-started/installation.md`.

If the upstream build changes again, the pages to update are: `install.astro`,
`getting-started/installation.md`, `getting-started/first-program.md`, `contributing.md`, and
the "Build it and run something" terminal block in `index.astro`.

## Positioning & copy decisions (keep these consistent)

These are deliberate and easy to undo by accident — preserve them:

- **Motto:** **"You Aren't Going To Write It."** (single line). The longer
  "...You Are Going To Read It." was removed as a *fixed tagline*, though the read-it *concept*
  still appears in prose (the blog post and docs intro blockquotes). Hero, page `<title>`, and
  OG image use the single line.
- **Core thesis:** written by AI, for AI — the AI writes the code, you read it, so the language
  optimizes for the *reader, not the typist*. Verbosity/explicitness is the product, not a
  cost. This is the answer to "but it's less concise."
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
- **Always `npm run build` before pushing** — a push auto-deploys, so a broken build is a
  broken deploy. `npx astro check` should report 0 errors too.
