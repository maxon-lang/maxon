import { existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { posix } from 'node:path';
import { glob, type Loader, type LoaderContext } from 'astro/loaders';
import { docsLoader } from '@astrojs/starlight/loaders';
import type { MdastPluginEntry, MdastVisitorContext } from 'satteri';
import type { Definition, Link } from 'mdast';

export const SHARED_DOCS = [{ dir: '../docs/internals', route: 'docs/internals' }] as const;

type SharedDoc = (typeof SHARED_DOCS)[number];

const REPOSITORY_BLOB = 'https://github.com/maxon-lang/maxon/blob/main/';
const PAGE_FILE = /^[a-z0-9]+(?:-[a-z0-9]+)*\.md$/;
const INDEX_PAGE = 'index.md';

const ownsId = (doc: SharedDoc, id: string) => id === doc.route || id.startsWith(`${doc.route}/`);
const isSharedId = (id: string) => SHARED_DOCS.some((doc) => ownsId(doc, id));

const pageRoute = (doc: SharedDoc, file: string) =>
  file === INDEX_PAGE ? `/${doc.route}/` : `/${doc.route}/${file.slice(0, -'.md'.length)}/`;

function requirePageFile(doc: SharedDoc, file: string): void {
  if (!PAGE_FILE.test(file)) {
    throw new Error(`${doc.dir}/${file}: a shared page is one lower-case, hyphenated .md file directly in ${doc.dir}`);
  }
}

function scopedStore(store: LoaderContext['store'], owns: (id: string) => boolean): LoaderContext['store'] {
  return new Proxy(store, {
    get(target, property) {
      if (property === 'keys') return () => [...target.keys()].filter(owns);
      const value = Reflect.get(target, property, target);
      return typeof value === 'function' ? value.bind(target) : value;
    },
  });
}

export function repositoryDocsLoader(): Loader {
  const starlightDocs = docsLoader();

  return {
    name: 'maxon-repository-docs-loader',
    async load(context) {
      for (const doc of SHARED_DOCS) {
        const shadow = new URL(`./src/content/docs/${doc.route}`, context.config.root);
        if (existsSync(shadow)) {
          throw new Error(`${fileURLToPath(shadow)} would shadow the pages read from ${doc.dir} — delete it`);
        }
      }

      await starlightDocs.load({ ...context, store: scopedStore(context.store, (id) => !isSharedId(id)) });

      for (const doc of SHARED_DOCS) {
        const pages = glob({
          base: doc.dir,
          pattern: '**/*.md',
          generateId: ({ entry }) => {
            requirePageFile(doc, entry);
            return pageRoute(doc, entry).slice(1, -1);
          },
        });
        await pages.load({ ...context, store: scopedStore(context.store, (id) => ownsId(doc, id)) });
      }
    },
  };
}

function resolveSharedLink(doc: SharedDoc, docDir: string, pagePath: string, url: string): string {
  if (url.startsWith('#') || /^[a-z][a-z0-9+.-]*:/i.test(url)) return url;

  if (url.startsWith('/')) {
    throw new Error(`${pagePath}: "${url}" is a site route, which is a dead link in the repository — link the file instead`);
  }

  const hashAt = url.indexOf('#');
  const target = hashAt === -1 ? url : url.slice(0, hashAt);
  const anchor = hashAt === -1 ? '' : url.slice(hashAt);

  if (!target.includes('/') && target.endsWith('.md')) {
    requirePageFile(doc, target);
    if (!existsSync(`${docDir}/${target}`)) throw new Error(`${pagePath}: "${url}" names no page in ${doc.dir}`);
    return pageRoute(doc, target) + anchor;
  }

  const repositoryPath = posix.normalize(posix.join(doc.dir.replace(/^\.\.\//, ''), target));
  if (repositoryPath.startsWith('../') || !existsSync(posix.join(docDir, target))) {
    throw new Error(`${pagePath}: "${url}" names no file in the repository`);
  }
  return REPOSITORY_BLOB + repositoryPath + anchor;
}

export function sharedDocsLinks(root: URL): MdastPluginEntry {
  const docs = SHARED_DOCS.map((doc) => ({ doc, dir: fileURLToPath(new URL(`${doc.dir}/`, root)).replace(/\\/g, '/') }));

  return ({ fileURL }) => {
    if (!fileURL) return null;
    const pagePath = fileURLToPath(fileURL).replace(/\\/g, '/');
    const owner = docs.find(({ dir }) => pagePath.startsWith(dir));
    if (!owner) return null;

    const rewrite = (node: Readonly<Link | Definition>, context: MdastVisitorContext) => {
      const url = resolveSharedLink(owner.doc, owner.dir, pagePath, node.url);
      if (url !== node.url) context.setProperty(node, 'url', url);
    };

    const pageFile = pagePath.slice(owner.dir.length);
    requirePageFile(owner.doc, pageFile);
    const route = pageRoute(owner.doc, pageFile);

    const recordSlug = (_root: unknown, context: MdastVisitorContext) => {
      const frontmatter = (context.data as { astro?: { frontmatter?: Record<string, unknown> } }).astro?.frontmatter;
      if (!frontmatter) throw new Error(`${pagePath}: Astro passed no frontmatter to record the page's route in`);
      frontmatter['slug'] = route.slice(1, -1);
    };

    return { name: 'maxon-shared-docs-links', before: recordSlug, link: rewrite, definition: rewrite };
  };
}
