// Refresh the site's copies of the compiler's own documentation.
//
// ⭐⭐ **THESE PAGES ARE COPIES, AND THEY ROT.** Four of them are 86–93% identical to a file in
// `../docs/`, and the drift is not cosmetic: the published site taught `var items IntArray = …`, a
// form with no `as` keyword that the compiler stopped accepting months earlier. Nobody noticed
// because keeping them in step was a thing a person had to remember.
//
// ⭐ **THE PAGE'S OWN FRONT MATTER IS PRESERVED, THE BODY IS REPLACED.** The `title`/`description`
// were written for a public audience and are not in the source; the prose is the compiler's and
// should never be edited here. So this rewrites everything below the front matter and nothing above
// it — which is also what makes the result reviewable as a diff.
//
// ⚠ **IT IS NOT RUN BY THE BUILD.** Re-syncing is a decision about WHEN the site should start
// describing new behaviour — `MAINTAINING.md` says to do it from a tagged release, so the published
// docs match shipped behaviour rather than in-progress work. Run it deliberately:
//
//     node website/scripts/sync-docs.mjs           # rewrite the pages
//     node website/scripts/sync-docs.mjs --check   # report drift, change nothing (exit 1 if any)
//
// ⛔ `language/*` IS NOT HERE. Those fifteen pages are one 217 KB `LANGUAGE_REFERENCE.md` split by
// editorial judgement, and a split is not a copy — it needs a section-to-page map that does not
// exist yet. They rot the same way and are the larger half of the problem.

import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const websiteDir = join(dirname(fileURLToPath(import.meta.url)), '..');
const repoRoot = join(websiteDir, '..');

// site page (relative to website/) → source (relative to the repo root)
const PAGES = [
  ['src/content/docs/docs/spec/bnf-syntax.md', 'docs/BNF_SYNTAX.md'],
  ['src/content/docs/docs/best-practices/writing-maxon-code.md', 'docs/WRITING_MAXON_CODE.md'],
  ['src/content/docs/docs/best-practices/best-practices.md', 'docs/BEST_PRACTICES.md'],
  ['src/content/docs/docs/stdlib/index.md', 'docs/STDLIB_REFERENCE.md'],
];

// ⛔ **SECTIONS THAT DESCRIBE THIS REPOSITORY, NOT THE LANGUAGE.** `MAINTAINING.md` says to trim
// compiler-repo-internal notes while keeping the agent-facing "how to write Maxon" framing, and this
// is where that rule is executed rather than remembered. A reader of the website has no `maxon-bin/`
// and no spec suite, so `./maxon-bin/.maxon/maxon.exe spec-test` is an instruction they cannot follow.
//
// Each entry drops one `## ` section and everything under it, up to the next `## `. A heading that is
// no longer there is a hard failure rather than a silent no-op — a renamed section would otherwise
// start publishing itself.
const DROP_SECTIONS = {
  'docs/WRITING_MAXON_CODE.md': ['Building and Testing'],
};

// Prose that survives the section trim but still names this checkout. A reader installed `maxon` and
// runs it by name.
const REWRITES = [
  [/`\.\/maxon-bin\/\.maxon\/maxon(\.exe)?`/g, '`maxon`'],
  [/`\.claude\/CLAUDE\.md` and `docs\/STYLE_GUIDE\.md#comments`/g, "the compiler repository's own style guide"],
];

// A repo-relative `X.md` link means nothing on a website. Each one becomes the route that page lives
// at. ⛔ A source link with no entry here is a hard failure: silently publishing a dead relative link
// is exactly the rot this script exists to stop, and `starlight-links-validator` cannot catch it
// because it is not an internal route.
const LINK_ROUTES = {
  'LANGUAGE_REFERENCE.md': '/docs/language/overview/',
  'BNF_SYNTAX.md': '/docs/spec/bnf-syntax/',
  'CLI_REFERENCE.md': '/docs/cli/',
  'STDLIB_REFERENCE.md': '/docs/stdlib/',
  'QUICK_REFERENCE.md': '/docs/language/overview/',
  'STYLE_GUIDE.md': '/docs/best-practices/writing-maxon-code/',
  'BEST_PRACTICES.md': '/docs/best-practices/best-practices/',
  'WRITING_MAXON_CODE.md': '/docs/best-practices/writing-maxon-code/',
};

function frontMatterOf(page, path) {
  if (!page.startsWith('---\n')) {
    throw new Error(`${path}: no front matter — this script only replaces bodies`);
  }
  const end = page.indexOf('\n---\n', 4);
  if (end === -1) throw new Error(`${path}: unterminated front matter`);
  return page.slice(0, end + 5);
}

function bodyFrom(source, sourceRel) {
  // ⚠ CRLF FIRST. The compiler repo is edited on Windows and these files carry CRLF; a later regex
  // anchored to an end-of-line would otherwise leave a stray carriage return inside a link or a fence.
  let text = source.replace(/\r\n/g, '\n');

  // The source's H1 is the page's `title`, which Starlight renders itself.
  text = text.replace(/^#[^#\n][^\n]*\n+/, '');

  for (const heading of DROP_SECTIONS[sourceRel] ?? []) {
    const marker = '\n## ' + heading + '\n';
    const start = text.indexOf(marker);
    if (start === -1) {
      throw new Error(`${sourceRel}: no "## ${heading}" section to drop — has it been renamed?`);
    }
    const after = text.indexOf('\n## ', start + 1);
    text = text.slice(0, start) + (after === -1 ? '\n' : text.slice(after));
  }

  for (const [pattern, replacement] of REWRITES) text = text.replace(pattern, replacement);

  text = text.replace(/\]\(([A-Z_]+\.md)(#[^)]*)?\)/g, (whole, file, anchor) => {
    const route = LINK_ROUTES[file];
    if (!route) throw new Error(`${sourceRel}: no route mapped for the link ${whole}`);
    return '](' + route + (anchor ?? '') + ')';
  });

  return text.trimEnd() + '\n';
}

const check = process.argv.includes('--check');
let drifted = 0;

for (const [pageRel, sourceRel] of PAGES) {
  const current = readFileSync(join(websiteDir, pageRel), 'utf-8').replace(/\r\n/g, '\n');
  const source = readFileSync(join(repoRoot, sourceRel), 'utf-8');
  const next = frontMatterOf(current, pageRel) + '\n' + bodyFrom(source, sourceRel);

  if (current === next) {
    console.log(`  up to date   ${pageRel}`);
    continue;
  }

  drifted += 1;
  if (check) {
    console.log(`  DRIFTED      ${pageRel}  (source: ${sourceRel})`);
  } else {
    writeFileSync(join(websiteDir, pageRel), next);
    console.log(`  rewrote      ${pageRel}  (from ${sourceRel})`);
  }
}

if (check && drifted > 0) {
  console.error(`\nsync-docs: ${drifted} page(s) no longer match ../docs/. Run without --check to refresh them.`);
  process.exit(1);
}
console.log(`\nsync-docs: ${drifted === 0 ? 'everything matches' : drifted + ' page(s) ' + (check ? 'drifted' : 'rewritten')}`);
