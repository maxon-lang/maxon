// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import starlightBlog from 'starlight-blog';
import starlightLlmsTxt from 'starlight-llms-txt';
import starlightLinksValidator from 'starlight-links-validator';
import tailwindcss from '@tailwindcss/vite';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

// The Maxon TextMate grammar is copied verbatim from the compiler repo's VS Code
// extension (src/grammars/maxon.tmLanguage.json, scope `source.maxon`). Registering
// it with Expressive Code / Shiki gives ```maxon fenced blocks the exact same
// highlighting as the editor, resolved at build time (zero client JS).
const maxonGrammar = JSON.parse(
  readFileSync(fileURLToPath(new URL('./src/grammars/maxon.tmLanguage.json', import.meta.url)), 'utf-8'),
);
// Shiki matches fenced-block languages against the grammar's lowercase `name`.
maxonGrammar.name = 'maxon';

export default defineConfig({
  site: 'https://maxon.dev',
  integrations: [
    starlight({
      title: 'Maxon',
      description: 'Maxon is a compiled programming language — written by AI, for AI.',
      logo: {
        src: './src/assets/logo.svg',
        alt: 'Maxon',
        replacesTitle: false,
      },
      social: [
        { icon: 'github', label: 'GitHub', href: 'https://github.com/maxon-lang/maxon' },
      ],
      plugins: [
        // Mixed-feed blog (release notes + essays) served at /blog.
        starlightBlog({
          title: 'Blog',
          postCount: 10,
          recentPostCount: 5,
          authors: {
            maxon: {
              name: 'The Maxon Project',
              title: 'Written by AI, for AI',
              url: 'https://maxon.dev',
            },
          },
        }),
        // The docs, flattened into /llms.txt and /llms-full.txt so an agent can read the
        // whole language in one fetch. For a language written by AI for AI, this is the
        // format its actual audience consumes.
        starlightLlmsTxt({
          projectName: 'Maxon',
          description:
            'Maxon is a compiled programming language — written by AI, for AI. One compiler, a from-scratch native backend, and a standard library it reads at build time.',
          optionalLinks: [
            {
              label: 'Source, releases and issues',
              url: 'https://github.com/maxon-lang/maxon',
              description: 'The compiler, standard library and tooling.',
            },
          ],
        }),
        // Fails the build on a broken internal link. The docs are re-synced from the
        // compiler repo by hand, so links rot silently otherwise.
        starlightLinksValidator({
          errorOnRelativeLinks: false,
          // The bespoke marketing pages are real routes, but they live outside Starlight
          // so the validator cannot see them and reports every link as a dead end.
          exclude: ['/install/', '/examples/', '/blog/'],
        }),
      ],
      // Per-page Open Graph cards. See src/route-data.ts.
      routeMiddleware: './src/route-data.ts',
      customCss: ['./src/styles/theme.css'],
      expressiveCode: {
        themes: ['github-dark', 'github-light'],
        shiki: {
          langs: [maxonGrammar],
        },
        styleOverrides: {
          borderColor: 'var(--mx-border)',
          borderRadius: '0.5rem',
        },
      },
      sidebar: [
        {
          label: 'Getting Started',
          items: [{ autogenerate: { directory: 'docs/getting-started' } }],
        },
        {
          // Sixteen pages, and the only group long enough to swamp the sidebar. Starlight
          // still opens it automatically for whichever page you are actually on.
          label: 'Language Reference',
          collapsed: true,
          items: [{ autogenerate: { directory: 'docs/language' } }],
        },
        {
          label: 'Standard Library',
          items: [{ autogenerate: { directory: 'docs/stdlib' } }],
        },
        {
          label: 'CLI',
          items: [{ autogenerate: { directory: 'docs/cli' } }],
        },
        {
          label: 'Best Practices',
          items: [{ autogenerate: { directory: 'docs/best-practices' } }],
        },
        {
          label: 'Specification',
          items: [{ autogenerate: { directory: 'docs/spec' } }],
        },
        {
          label: 'Project',
          items: [
            { label: 'Changelog', link: '/docs/changelog/' },
            { label: 'Contributing', link: '/docs/contributing/' },
            { label: 'About', link: '/docs/about/' },
            { label: 'Source on GitHub', link: 'https://github.com/maxon-lang/maxon' },
          ],
        },
      ],
    }),
  ],
  // Tailwind v4 is a Vite plugin, not an Astro integration. Base styles are pulled in
  // by `@import 'tailwindcss'` in src/styles/global.css, which only the marketing
  // layout loads — Starlight's docs keep their own reset.
  vite: {
    plugins: [tailwindcss()],
  },
});
