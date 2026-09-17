// Generate the site's reference pages from the repository's own documentation.
//
//     node website/scripts/sync-docs.mjs           # rewrite the pages
//     node website/scripts/sync-docs.mjs --check   # report drift, change nothing (exit 1 if any)
//
// A page keeps its front matter and has its body replaced: `title`/`description`/`sidebar` are written
// for the site and exist nowhere else, while the prose belongs to the source and is never edited here.
//
// Every rule below FAILS CLOSED, and every problem is reported before anything is written. A heading no
// page claims, a link to a heading that does not exist, or a page file no source produces would each
// otherwise publish something wrong without a sound — prose silently dropped, a dead anchor, or a stale
// hand-written page.

import { readFileSync, writeFileSync, readdirSync } from 'node:fs';
import { createRequire } from 'node:module';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { basename, dirname, join } from 'node:path';

const websiteDir = join(dirname(fileURLToPath(import.meta.url)), '..');
const repoRoot = join(websiteDir, '..');
const docsCollectionDir = 'src/content/docs/docs';
const SOURCES_DIR = 'docs';
const FRONT_MATTER_FENCE = '---\n';

// The text between a source's H1 and its first `## `, claimable like a section.
const PREAMBLE = Symbol('preamble');
// Every part of the source that `notPublished` does not name, in source order, headings kept.
const EVERY_SECTION = Symbol('every section');
// A page path always ends in `.md`, so this owner cannot be mistaken for one.
const NOT_PUBLISHED = 'notPublished';

// Page paths are relative to the docs collection. A page listing exactly one `## ` heading omits that
// heading and promotes the headings under it one level: the page title already names the section, and
// Starlight's table of contents starts at `##`.
const SOURCES = [
  {
    source: 'docs/CLI_REFERENCE.md',
    pages: [
      { page: 'cli/index.md', sections: [PREAMBLE, 'Commands'] },
      { page: 'cli/project-structure.md', sections: ['Project Structure'] },
      { page: 'cli/debugging.md', sections: ['Debugging and Profiling'] },
      { page: 'cli/editor.md', sections: ['Editor Support'] },
      { page: 'cli/targets.md', sections: ['Targets'] },
      { page: 'cli/mcp-server.md', sections: ['MCP Server'] },
      { page: 'cli/compiler-development.md', sections: ['Working on the Compiler'] },
    ],
    notPublished: [],
  },
  {
    source: 'docs/LANGUAGE_REFERENCE.md',
    pages: [
      { page: 'language/overview.md', sections: [PREAMBLE, 'Program Structure', 'Lexical Elements'] },
      { page: 'language/types.md', sections: ['Types'] },
      { page: 'language/ranged-typealiases.md', sections: ['Ranged Type Aliases'] },
      { page: 'language/composite-types.md', sections: ['Composite Types', 'Tuples'] },
      { page: 'language/enums-unions.md', sections: ['Enums', 'Raw-Value Enums', 'Unions'] },
      { page: 'language/variables.md', sections: ['Variables'] },
      { page: 'language/functions.md', sections: ['Functions'] },
      { page: 'language/expressions.md', sections: ['Expressions'] },
      { page: 'language/statements.md', sections: ['Statements'] },
      { page: 'language/error-handling.md', sections: ['Error Handling'] },
      { page: 'language/testing.md', sections: ['Testing'] },
      { page: 'language/namespaces.md', sections: ['Namespaces'] },
      { page: 'language/async.md', sections: ['Concurrency'] },
      { page: 'language/build-system.md', sections: ['Build System'] },
      { page: 'language/memory-model.md', sections: ['Memory Model', 'Code Generation'] },
      {
        page: 'language/patterns-and-errors.md',
        sections: ['Common Patterns', 'Common Errors', 'Best Practices for AI Agents'],
      },
    ],
    notPublished: ['The Runtime Tier'],
  },
  {
    source: 'docs/STDLIB_REFERENCE.md',
    pages: [
      { page: 'stdlib/index.md', sections: ['Overview', 'Core Functions'] },
      { page: 'stdlib/text.md', sections: ['String', 'Character', 'Ascii', 'Unicode', 'CharacterSet'] },
      {
        page: 'stdlib/collections.md',
        sections: ['Array', 'List', 'Map', 'Set', 'Vector', 'Range', 'Iterators', 'Interfaces'],
      },
      {
        page: 'stdlib/io.md',
        sections: ['File', 'FilePath', 'Directory', 'Console', 'CommandLine', 'Log', 'Process', 'Subprocess', 'SharedMemory'],
      },
      { page: 'stdlib/network.md', sections: ['TcpClient', 'TcpListener', 'HttpClient', 'URL'] },
      { page: 'stdlib/data.md', sections: ['Json', 'Sha256', 'Hasher'] },
      { page: 'stdlib/runtime.md', sections: ['Clock', 'Runtime', 'Math', 'Primitive Extensions'] },
      { page: 'stdlib/testing.md', sections: ['Testing'] },
      { page: 'stdlib/build.md', sections: ['Build'] },
    ],
    notPublished: [],
  },
  {
    source: 'docs/BNF_SYNTAX.md',
    pages: [{ page: 'spec/bnf-syntax.md', sections: EVERY_SECTION }],
    notPublished: [],
  },
  {
    source: 'docs/WRITING_MAXON_CODE.md',
    pages: [{ page: 'best-practices/writing-maxon-code.md', sections: EVERY_SECTION }],
    // It describes this repository's build, which a reader who installed `maxon` does not have.
    notPublished: ['Building and Testing'],
  },
  {
    source: 'docs/BEST_PRACTICES.md',
    pages: [{ page: 'best-practices/best-practices.md', sections: EVERY_SECTION }],
    notPublished: [],
  },
];

const ERROR_CODES_PAGE = 'cli/error-codes.md';
const ERROR_CODE_REGISTRY = 'maxon-bin/Compiler/ErrorCodeRegistry.maxon';
const ERROR_CODE_REGISTRY_LINK = '../' + ERROR_CODE_REGISTRY;
// The stage names are the ones `lookup_error_code` answers with, so the page cannot disagree with it.
const STAGE_BANDS_SOURCE = 'maxon-bin/Compiler/Mcp/McpErrorCodes.maxon';

// Every file in these directories must be a page this script produces; anything else is hand-written
// content that no sync would ever correct.
const GENERATED_DIRS = ['cli', 'language', 'stdlib', 'spec', 'best-practices'];

// Documents with no page of their own. A link into one of them with an anchor has nowhere true to point.
const UNPUBLISHED_DOC_ROUTES = {
  'QUICK_REFERENCE.md': '/docs/language/overview/',
  'STYLE_GUIDE.md': '/docs/best-practices/writing-maxon-code/',
};

// Prose that still names this checkout. A reader installed `maxon` and runs it by name.
const REWRITES = [
  [/`\.\/maxon-bin\/\.maxon\/maxon(\.exe)?`/g, '`maxon`'],
  [/`\.claude\/CLAUDE\.md` and `docs\/STYLE_GUIDE\.md#comments`/g, "the compiler repository's own style guide"],
  [/the `documenter` skill/g, 'a dedicated documentation step'],
];

const applyRewrites = (text) => REWRITES.reduce((result, [pattern, replacement]) => result.replace(pattern, replacement), text);

const problems = [];
const fail = (message) => problems.push(message);

// Heading ids come from the markdown processor the site builds with, configured as Starlight configures
// it: smart punctuation and directives both change a heading's text, and so its slug. Anchors written in
// a source file follow GitHub's rendering, which has neither.
async function loadRenderers() {
  const astroEntry = fileURLToPath(import.meta.resolve('astro'));
  const satteriEntry = createRequire(astroEntry).resolve('@astrojs/markdown-satteri');
  const { createSatteriMarkdownProcessor } = await import(pathToFileURL(satteriEntry).href);
  const starlightSatteri = new URL('./integrations/satteri.js', import.meta.resolve('@astrojs/starlight'));
  const { satteriDirectivesRestoration } = await import(starlightSatteri.href);

  const site = await createSatteriMarkdownProcessor({
    syntaxHighlight: false,
    features: { directive: true },
    mdastPlugins: [satteriDirectivesRestoration()],
  });
  const github = await createSatteriMarkdownProcessor({
    syntaxHighlight: false,
    features: { smartPunctuation: false },
  });

  return {
    siteHeadings: async (text) => (await site.render(text)).metadata.headings,
    githubHeadings: async (text) => (await github.render(text)).metadata.headings,
  };
}

const readText = (path) => readFileSync(path, 'utf-8').replace(/\r\n/g, '\n');
const readRepoText = (rel) => readText(join(repoRoot, rel));

// ATX headings and fenced-code membership per line. The result is checked against the markdown parser
// wherever it matters, so a construct this scanner misreads is reported rather than mis-split.
function scanMarkdown(lines) {
  const headings = [];
  const fenced = [];
  let fence = null;

  lines.forEach((line, index) => {
    const fenceMatch = /^ {0,3}(`{3,}|~{3,})(.*)$/.exec(line);

    if (fence) {
      fenced.push(true);
      const closes = fenceMatch && fenceMatch[1][0] === fence[0] && fenceMatch[1].length >= fence.length && fenceMatch[2].trim() === '';
      if (closes) fence = null;
      return;
    }

    if (fenceMatch && !(fenceMatch[1][0] === '`' && fenceMatch[2].includes('`'))) {
      fenced.push(true);
      fence = fenceMatch[1];
      return;
    }

    fenced.push(false);
    const heading = /^ {0,3}(#{1,6})(?:[ \t]+(.*?))?(?:[ \t]+#+)?[ \t]*$/.exec(line);
    if (heading) headings.push({ index, depth: heading[1].length, text: (heading[2] ?? '').trim() });
  });

  return { headings, fenced };
}

// Each scanned heading is rendered on its own to learn the text the parser gives it, so a heading the
// scanner missed cannot be hidden by one it invented at the same depth.
async function firstDisagreement(scanned, parsed, renderHeadings) {
  const count = Math.max(scanned.length, parsed.length);
  for (let i = 0; i < count; i += 1) {
    const alone = scanned[i] ? (await renderHeadings(`${'#'.repeat(scanned[i].depth)} ${scanned[i].text}`))[0] : undefined;
    if (scanned[i]?.depth !== parsed[i]?.depth || alone?.text !== parsed[i]?.text) {
      const ours = scanned[i] ? `line ${scanned[i].index + 1} "${'#'.repeat(scanned[i].depth)} ${scanned[i].text}"` : 'nothing';
      const theirs = parsed[i] ? `h${parsed[i].depth} "${parsed[i].text}"` : 'nothing';
      return `heading #${i + 1}: the scanner sees ${ours}, the markdown parser sees ${theirs}`;
    }
  }
  return null;
}

// Backtick code-span ranges, paired as CommonMark pairs them: a run closes only on a run of equal length.
function codeSpanRanges(line) {
  const ranges = [];
  let i = 0;

  while (i < line.length) {
    if (line[i] !== '`') {
      i += 1;
      continue;
    }

    let runEnd = i;
    while (line[runEnd] === '`') runEnd += 1;
    const run = runEnd - i;

    let search = runEnd;
    let closed = false;
    while (search < line.length) {
      if (line[search] !== '`') {
        search += 1;
        continue;
      }
      let closeEnd = search;
      while (line[closeEnd] === '`') closeEnd += 1;
      if (closeEnd - search === run) {
        ranges.push([i, closeEnd]);
        i = closeEnd;
        closed = true;
        break;
      }
      search = closeEnd;
    }

    if (!closed) i = runEnd;
  }

  return ranges;
}

const insideRanges = (ranges, offset) => ranges.some(([start, end]) => offset >= start && offset < end);

const isBlank = (lines) => lines.every((line) => line.trim() === '');

// Leading/trailing blank lines and a closing thematic break go: the break separates sections in the
// source, and at the end of a page or between two parts it separates nothing.
function trimPart(lines, fenced, start, end) {
  let first = start;
  let last = end;

  for (;;) {
    while (first < last && lines[first].trim() === '') first += 1;
    while (last > first && lines[last - 1].trim() === '') last -= 1;

    const closesWithBreak =
      last > first && !fenced[last - 1] && /^ {0,3}([-*_])( *\1){2,} *$/.test(lines[last - 1]) && (last - 1 === first || lines[last - 2].trim() === '');
    if (!closesWithBreak) break;
    last -= 1;
  }

  return { first, last };
}

const describePart = (key) => (key === PREAMBLE ? 'the preamble (the text between the H1 and the first ## heading)' : `"## ${key}"`);

function routeOf(pageRel) {
  const withoutExtension = pageRel.replace(/\.mdx?$/, '');
  const route = withoutExtension.endsWith('/index') ? withoutExtension.slice(0, -'index'.length) : withoutExtension + '/';
  return '/docs/' + route;
}

async function modelSource(spec, renderers) {
  const text = readRepoText(spec.source);
  const lines = text.split('\n');
  const { headings, fenced } = scanMarkdown(lines);

  const parsed = await renderers.githubHeadings(text);
  const disagreement = await firstDisagreement(headings, parsed, renderers.githubHeadings);
  if (disagreement) {
    fail(`${spec.source}: ${disagreement}. Write the heading as a plain ATX heading outside any block.`);
    return null;
  }
  headings.forEach((heading, i) => (heading.sourceSlug = parsed[i].slug));

  const titles = headings.filter((heading) => heading.depth === 1);
  if (titles.length !== 1 || headings[0] !== titles[0] || !isBlank(lines.slice(0, titles[0].index))) {
    fail(`${spec.source}: must open with its one "# " title, which becomes the page title (found ${titles.length} H1 heading(s))`);
    return null;
  }

  const sectionHeadings = headings.filter((heading) => heading.depth === 2);
  const parts = [{ key: PREAMBLE, start: titles[0].index + 1, end: sectionHeadings[0]?.index ?? lines.length }];
  sectionHeadings.forEach((heading, i) => {
    parts.push({ key: heading.text, start: heading.index, end: sectionHeadings[i + 1]?.index ?? lines.length });
  });

  const partsByKey = new Map();
  for (const part of parts) {
    if (partsByKey.has(part.key)) {
      fail(`${spec.source}: ${describePart(part.key)} appears more than once, so a page cannot claim it by name`);
      return null;
    }
    const { first, last } = trimPart(lines, fenced, part.start, part.end);
    part.first = first;
    part.last = last;
    part.blank = first === last;
    part.headings = headings.filter((heading) => heading.depth > 1 && heading.index >= first && heading.index < last);
    partsByKey.set(part.key, part);
  }

  const owner = new Map();
  const claim = (key, claimant) => {
    const part = partsByKey.get(key);
    if (!part || part.blank) {
      fail(`${spec.source}: ${claimant} lists ${describePart(key)}, which the source does not have`);
      return;
    }
    if (owner.has(key)) {
      fail(`${spec.source}: ${describePart(key)} is claimed by both ${owner.get(key)} and ${claimant}`);
      return;
    }
    owner.set(key, claimant);
  };

  for (const key of spec.notPublished) claim(key, NOT_PUBLISHED);

  const everySectionPages = spec.pages.filter((page) => page.sections === EVERY_SECTION);
  if (everySectionPages.length > 0 && spec.pages.length > 1) {
    fail(`${spec.source}: a page taking EVERY_SECTION must be the source's only page`);
    return null;
  }

  for (const page of spec.pages) {
    if (page.sections !== EVERY_SECTION) for (const key of page.sections) claim(key, page.page);
  }

  for (const part of parts) {
    if (owner.has(part.key) || part.blank) continue;
    if (everySectionPages.length === 1) {
      owner.set(part.key, everySectionPages[0].page);
    } else {
      fail(`${spec.source}: ${describePart(part.key)} is not claimed by any page and is not listed in notPublished`);
    }
  }

  return { spec, lines, headings, parts, partsByKey, owner };
}

// Builds each page's body with its headings, and indexes every source heading's anchor to where it lands.
async function assemblePages(model, renderers) {
  const { spec, lines, headings, parts, partsByKey, owner } = model;
  model.anchors = new Map();
  model.pages = [];

  for (const page of spec.pages) {
    const keys = page.sections === EVERY_SECTION ? parts.filter((part) => owner.get(part.key) === page.page).map((part) => part.key) : page.sections;
    const headingKeys = page.sections === EVERY_SECTION ? [] : keys.filter((key) => key !== PREAMBLE);
    const titleSection = headingKeys.length === 1 ? headingKeys[0] : null;

    const blocks = [];
    const expected = [];

    for (const key of keys) {
      const part = partsByKey.get(key);
      if (!part || owner.get(key) !== page.page) continue;

      const block = [];
      for (let index = part.first; index < part.last; index += 1) {
        const heading = part.headings.find((candidate) => candidate.index === index);

        if (heading && key === titleSection && heading.depth === 2) {
          model.anchors.set(heading.sourceSlug, { page: page.page, slug: null });
          continue;
        }

        if (heading && key === titleSection) {
          block.push(lines[index].replace(/^( {0,3})#/, '$1'));
          expected.push({ heading, depth: heading.depth - 1 });
          continue;
        }

        if (heading) expected.push({ heading, depth: heading.depth });
        block.push(lines[index]);
      }

      const trimmed = block.join('\n').trim();
      if (trimmed !== '') blocks.push(trimmed);
    }

    const body = applyRewrites(blocks.join('\n\n'));
    const rendered = await renderers.siteHeadings(body);
    const disagreement = await firstDisagreement(
      expected.map(({ heading, depth }) => ({ index: heading.index, depth, text: applyRewrites(heading.text) })),
      rendered,
      renderers.siteHeadings,
    );
    if (disagreement) {
      fail(`${page.page} (from ${spec.source}): assembling the page changed its headings — ${disagreement} (line numbers are the source's)`);
      expected.forEach(({ heading }) => model.anchors.set(heading.sourceSlug, { page: page.page, slug: null, failed: true }));
      continue;
    }

    expected.forEach(({ heading }, i) => model.anchors.set(heading.sourceSlug, { page: page.page, slug: rendered[i].slug }));
    model.pages.push({ page: page.page, body });
  }

  headings.filter((heading) => heading.depth === 1).forEach((heading) => model.anchors.set(heading.sourceSlug, { page: spec.pages[0].page, slug: null }));

  for (const part of parts) {
    if (owner.get(part.key) !== NOT_PUBLISHED) continue;
    for (const heading of part.headings) model.anchors.set(heading.sourceSlug, { page: null, slug: null, unpublished: part.key });
  }
}

const splitAnchor = (target) => {
  const hashAt = target.indexOf('#');
  return hashAt === -1 ? [target, null] : [target.slice(0, hashAt), target.slice(hashAt + 1)];
};

// The Error Codes page is linked through the file it is generated from, which is also where a reader of
// the repository finds the codes; the anchor is the lower-cased code.
function resolveErrorCodeLink(target, anchor, where, site) {
  if (anchor === null) return routeOf(ERROR_CODES_PAGE);

  const slug = site.errorCodeSlugs.get(anchor);
  if (slug === undefined) {
    fail(`${where}: "${target}" names no code the registry declares (write the code in lower case, e.g. #e3014)`);
    return target;
  }
  return `${routeOf(ERROR_CODES_PAGE)}#${slug}`;
}

function resolveLink(target, model, pageRel, site) {
  const where = `${pageRel} (from ${model.spec.source})`;

  // A source is read in the repository too, where a site route is a dead link. Only pages written for the
  // site alone, which have no source to link through, are linked by route.
  if (target.startsWith('/')) {
    if (GENERATED_DIRS.some((dir) => target.startsWith(`/docs/${dir}/`))) {
      fail(`${where}: "${target}" is a route to a generated page — link its source instead (#anchor, FILE.md#anchor, or ${ERROR_CODE_REGISTRY_LINK}#eXXXX)`);
    }
    return target;
  }
  if (/^[a-z][a-z0-9+.-]*:/i.test(target)) return target;

  const [file, anchor] = splitAnchor(target);

  if (file === ERROR_CODE_REGISTRY_LINK) return resolveErrorCodeLink(target, anchor, where, site);

  if (file !== '' && !/^[A-Za-z0-9_-]+\.md$/.test(file)) {
    fail(`${where}: the relative link "${target}" has no route on the site`);
    return target;
  }

  const targetModel = file === '' ? model : site.modelsByFile.get(file);
  if (targetModel === null) return target;

  if (targetModel === undefined) {
    const route = UNPUBLISHED_DOC_ROUTES[file];
    if (!route) {
      fail(`${where}: no route mapped for the link "${target}"`);
    } else if (anchor !== null) {
      fail(`${where}: "${target}" links into ${file}, which has no page of its own to hold that anchor`);
    }
    return route ?? target;
  }

  if (anchor === null) return routeOf(targetModel.spec.pages[0].page);

  const landing = targetModel.anchors.get(anchor);
  if (!landing) {
    fail(`${where}: "${target}" names an anchor no heading in ${targetModel.spec.source} produces`);
    return target;
  }
  if (landing.failed) return target;
  if (landing.unpublished !== undefined) {
    fail(`${where}: "${target}" links into ${describePart(landing.unpublished)}, which is not published`);
    return target;
  }

  if (landing.page === pageRel) return '#' + (landing.slug ?? '_top');
  return routeOf(landing.page) + (landing.slug === null ? '' : '#' + landing.slug);
}

function rewriteLinks(body, model, pageRel, site) {
  const lines = body.split('\n');
  const { fenced } = scanMarkdown(lines);

  return lines
    .map((line, index) => {
      if (fenced[index]) return line;

      const definition = /^( {0,3}\[[^\]]+\]:[ \t]*)(\S+)(.*)$/.exec(line);
      if (definition) return definition[1] + resolveLink(definition[2], model, pageRel, site) + definition[3];

      const spans = codeSpanRanges(line);
      return line.replace(/\]\(([^()\s]+)((?:\s+"[^"]*")?)\)/g, (whole, target, title, offset) =>
        insideRanges(spans, offset) ? whole : '](' + resolveLink(target, model, pageRel, site) + title + ')',
      );
    })
    .join('\n');
}

function readStageBands() {
  const text = readRepoText(STAGE_BANDS_SOURCE);
  const start = text.indexOf('function stageForCode(');
  const end = text.indexOf("end 'stageForCode'", start);
  const bands = new Map();

  if (start === -1 || end === -1) {
    fail(`${STAGE_BANDS_SOURCE}: no stageForCode function to read the stage bands from`);
    return bands;
  }

  for (const line of text.slice(start, end).split('\n')) {
    const arm = /^\s*(\d) gives "([^"]+)"\s*$/.exec(line);
    if (!arm) continue;
    const named = /^(.*?) \((.+)\)$/.exec(arm[2]);
    bands.set(arm[1], named ? { name: named[1], scope: named[2] } : { name: arm[2], scope: null });
  }

  if (bands.size === 0) fail(`${STAGE_BANDS_SOURCE}: stageForCode has no \`<digit> gives "<stage>"\` arms to read`);
  return bands;
}

// The registry's `//` run directly above a case is that case's documentation, as `lookup_error_code`
// reads it.
function readRegistry() {
  const lines = readRepoText(ERROR_CODE_REGISTRY).split('\n');
  const open = lines.findIndex((line) => /^export enum ErrorCode\s*$/.test(line));
  const close = lines.findIndex((line) => /^end 'ErrorCode'\s*$/.test(line));
  const cases = [];

  if (open === -1 || close < open) {
    fail(`${ERROR_CODE_REGISTRY}: no "export enum ErrorCode" … "end 'ErrorCode'" block`);
    return cases;
  }

  let comment = [];
  let commentStart = -1;

  for (let index = open + 1; index < close; index += 1) {
    const line = lines[index].trim();
    const where = `${ERROR_CODE_REGISTRY}:${index + 1}`;

    if (line === '') {
      if (comment.length > 0) fail(`${where}: the comment starting on line ${commentStart + 1} documents no case — a blank line separates it from the next one`);
      comment = [];
      continue;
    }

    if (line.startsWith('//')) {
      if (comment.length === 0) commentStart = index;
      comment.push(lines[index].replace(/^\s*\/\/ ?/, ''));
      continue;
    }

    const member = /^([a-z][A-Za-z0-9]*) = "(E\d{4})"$/.exec(line);
    if (!member) {
      fail(`${where}: not a blank line, a // comment or a \`name = "EXXXX"\` case: ${line}`);
      comment = [];
      continue;
    }

    if (comment.length === 0) fail(`${where}: ${member[2]} (${member[1]}) has no doc comment`);
    cases.push({ name: member[1], code: member[2], doc: comment, line: index + 1 });
    comment = [];
  }

  if (comment.length > 0) fail(`${ERROR_CODE_REGISTRY}:${commentStart + 1}: the comment documents no case`);

  for (const field of ['code', 'name']) {
    const seen = new Map();
    for (const entry of cases) {
      if (seen.has(entry[field])) fail(`${ERROR_CODE_REGISTRY}: ${entry[field]} is declared on lines ${seen.get(entry[field])} and ${entry.line}`);
      else seen.set(entry[field], entry.line);
    }
  }

  return cases;
}

// Registry comments are plain prose with backtick code, not markdown: everything outside a code span is
// escaped so it renders as written.
function escapeProse(text) {
  const spans = codeSpanRanges(text);
  let out = '';

  for (let i = 0; i < text.length; i += 1) {
    const span = spans.find(([start]) => start === i);
    if (span) {
      out += text.slice(span[0], span[1]);
      i = span[1] - 1;
      continue;
    }
    // `:` before a letter opens a directive, which Starlight's markdown enables.
    const special = /[\\`*_[\]<>#|~&]/.test(text[i]) || (text[i] === ':' && /[A-Za-z]/.test(text[i + 1] ?? ''));
    out += special ? '\\' + text[i] : text[i];
  }

  // A block opening like a list marker would become a list.
  return out.replace(/^[-+=]/, '\\$&').replace(/^(\d+)([.)])/, '$1\\$2');
}

// A `//` line with no text separates paragraphs; a line opening with `- ` or `* ` starts a list item,
// and the lines after it continue that item until the next item or paragraph break.
function docToMarkdown(docLines) {
  const blocks = [];
  let paragraph = null;
  let list = null;

  const flush = () => {
    if (paragraph) blocks.push(escapeProse(paragraph.join(' ')));
    if (list) blocks.push(list.map((item) => '- ' + escapeProse(item.join(' '))).join('\n'));
    paragraph = null;
    list = null;
  };

  for (const raw of docLines) {
    const line = raw.trim();

    if (line === '') {
      flush();
      continue;
    }

    const item = /^[-*]\s+(.*)$/.exec(line);
    if (item) {
      if (paragraph) flush();
      list ??= [];
      list.push([item[1]]);
      continue;
    }

    if (list) list[list.length - 1].push(line);
    else (paragraph ??= []).push(line);
  }

  flush();
  return blocks.join('\n\n');
}

function errorCodesBody() {
  const bands = readStageBands();
  const cases = readRegistry().sort((a, b) => a.code.localeCompare(b.code));

  const sections = [
    'Every diagnostic the compiler reports carries a code, as in `error E3014: path:line:column: message`. ' +
      'The leading digit names the compilation stage that raised it. This page lists every code the compiler ' +
      'defines, grouped by stage, with the explanation its error-code registry records. The MCP server\'s ' +
      '`lookup_error_code` tool looks up one code, or its case name, the same way.',
  ];

  for (const [digit, band] of [...bands].sort(([a], [b]) => a.localeCompare(b))) {
    const members = cases.filter((entry) => entry.code[1] === digit);
    if (members.length === 0) continue;

    const scope = band.scope === null ? [] : [band.scope[0].toUpperCase() + band.scope.slice(1) + '.'];
    sections.push([`## ${band.name} (E${digit}xxx)`, ...scope].join('\n\n'));

    for (const entry of members) sections.push(`### ${entry.code} — \`${entry.name}\`\n\n${docToMarkdown(entry.doc)}`);
  }

  for (const entry of cases) {
    if (!bands.has(entry.code[1])) fail(`${ERROR_CODE_REGISTRY}:${entry.line}: ${entry.code} has a leading digit no stage band in ${STAGE_BANDS_SOURCE} names`);
  }

  return sections.join('\n\n');
}

function frontMatterOf(pageRel) {
  let page;
  try {
    page = readText(join(websiteDir, docsCollectionDir, pageRel));
  } catch {
    fail(`${pageRel}: the page does not exist — create it with front matter (title, description, sidebar.order)`);
    return null;
  }

  const fenceAt = page.startsWith(FRONT_MATTER_FENCE) ? page.indexOf('\n' + FRONT_MATTER_FENCE, FRONT_MATTER_FENCE.length) : -1;
  if (fenceAt === -1) {
    fail(`${pageRel}: no front matter — this script replaces bodies and never writes a page's title`);
    return null;
  }
  return { current: page, frontMatter: page.slice(0, fenceAt + 1 + FRONT_MATTER_FENCE.length) };
}

async function main() {
  const check = process.argv.includes('--check');
  const renderers = await loadRenderers();

  // A source that already failed maps to null, so links into it are not reported a second time.
  const site = { modelsByFile: new Map(), errorCodeSlugs: new Map() };
  const models = [];

  for (const spec of SOURCES) {
    if (dirname(spec.source) !== SOURCES_DIR) throw new Error(`${spec.source}: sources live in ${SOURCES_DIR}/, which ${ERROR_CODE_REGISTRY_LINK} is relative to`);

    const model = await modelSource(spec, renderers);
    site.modelsByFile.set(basename(spec.source), model);
    if (model) models.push(model);
  }

  for (const model of models) await assemblePages(model, renderers);

  const errorCodes = errorCodesBody();
  for (const heading of await renderers.siteHeadings(errorCodes)) {
    const code = /^(E\d{4}) /.exec(heading.text);
    if (code) site.errorCodeSlugs.set(code[1].toLowerCase(), heading.slug);
  }

  const outputs = [{ page: ERROR_CODES_PAGE, from: ERROR_CODE_REGISTRY, body: errorCodes }];
  for (const model of models) {
    for (const page of model.pages) {
      outputs.push({ page: page.page, from: model.spec.source, body: rewriteLinks(page.body, model, page.page, site) });
    }
  }

  const produced = new Set();
  for (const output of outputs) {
    if (produced.has(output.page)) fail(`${output.page}: produced by more than one source`);
    produced.add(output.page);
  }
  for (const spec of SOURCES) {
    for (const page of spec.pages) produced.add(page.page);
  }

  for (const dir of GENERATED_DIRS) {
    for (const entry of readdirSync(join(websiteDir, docsCollectionDir, dir))) {
      if (!produced.has(`${dir}/${entry}`)) fail(`${dir}/${entry}: no source produces this page — map it in SOURCES or move it out of ${dir}/`);
    }
  }

  const writes = [];
  for (const output of outputs) {
    const page = frontMatterOf(output.page);
    if (page) writes.push({ ...output, current: page.current, next: page.frontMatter + '\n' + output.body.trimEnd() + '\n' });
  }

  if (problems.length > 0) {
    console.error(`sync-docs: ${problems.length} problem(s); nothing was written.\n`);
    for (const problem of problems) console.error('  ' + problem);
    process.exit(1);
  }

  let drifted = 0;
  for (const { page, from, current, next } of writes) {
    if (current === next) {
      console.log(`  up to date   ${page}`);
      continue;
    }

    drifted += 1;
    if (check) {
      console.log(`  DRIFTED      ${page}  (source: ${from})`);
    } else {
      writeFileSync(join(websiteDir, docsCollectionDir, page), next);
      console.log(`  rewrote      ${page}  (from ${from})`);
    }
  }

  if (check && drifted > 0) {
    console.error(`\nsync-docs: ${drifted} page(s) no longer match their sources. Run without --check to regenerate them.`);
    process.exit(1);
  }
  console.log(`\nsync-docs: ${drifted === 0 ? 'everything matches' : drifted + ' page(s) rewritten'}`);
}

await main();
