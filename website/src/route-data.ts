/**
 * Starlight route middleware (wired up via `routeMiddleware` in astro.config.mjs).
 *
 * Starlight sets `twitter:card: summary_large_image` on every docs page but never an
 * `og:image`, so shared links rendered as blank cards. This points each page at its
 * own build-time card from src/pages/og/[...slug].png.ts, falling back to the
 * site-wide public/og.png for Starlight's virtual pages (the blog index, tag and
 * author listings) which have no content entry of their own.
 */
import { defineRouteMiddleware } from '@astrojs/starlight/route-data';
import { getCollection } from 'astro:content';

const SITE = 'https://maxon.dev';

// Which ids actually have a card. Resolved once and shared by every page in the build.
let cardIds: Promise<Set<string>> | undefined;
function knownCards(): Promise<Set<string>> {
  cardIds ??= getCollection('docs').then((entries) => new Set(entries.map((e) => e.id)));
  return cardIds;
}

export const onRequest = defineRouteMiddleware(async (context) => {
  const { entry, head } = context.locals.starlightRoute;

  const id = entry?.id;
  const path = id && (await knownCards()).has(id) ? `/og/${id}.png` : '/og.png';
  const image = new URL(path, context.site ?? SITE).href;

  head.push(
    { tag: 'meta', attrs: { property: 'og:image', content: image } },
    { tag: 'meta', attrs: { property: 'og:image:width', content: '1200' } },
    { tag: 'meta', attrs: { property: 'og:image:height', content: '630' } },
    {
      tag: 'meta',
      attrs: { property: 'og:image:alt', content: `${entry?.data.title ?? 'Maxon'} — Maxon` },
    },
    { tag: 'meta', attrs: { name: 'twitter:image', content: image } },
  );
});
