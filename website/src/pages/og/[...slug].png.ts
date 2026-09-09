/**
 * One Open Graph card per docs page, at /og/<entry id>.png.
 *
 * The site is statically output, so every card is rasterised at build time and the
 * route ships as a plain PNG — see src/lib/og-card.ts for the drawing, and
 * src/route-data.ts for the middleware that points each page's meta tags here.
 */
import type { APIRoute, GetStaticPaths } from 'astro';
import { getCollection } from 'astro:content';
import { ogPng, sectionFor } from '../../lib/og-card';

export const getStaticPaths: GetStaticPaths = async () => {
  const entries = await getCollection('docs');
  return entries.map((entry) => ({
    params: { slug: entry.id },
    props: { title: entry.data.title, section: sectionFor(entry.id) },
  }));
};

export const GET: APIRoute = async ({ props }) => {
  const { title, section } = props as { title: string; section: string };
  const png = await ogPng({ title, section });
  return new Response(new Uint8Array(png), {
    headers: {
      'Content-Type': 'image/png',
      'Cache-Control': 'public, max-age=31536000, immutable',
    },
  });
};
