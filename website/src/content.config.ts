import { defineCollection } from 'astro:content';
// Astro 7 deprecates the `z` re-export from `astro:content`; `astro/zod` is the
// supported path and stays pinned to the Zod version Astro itself validates with.
import { z } from 'astro/zod';
import { docsLoader } from '@astrojs/starlight/loaders';
import { docsSchema } from '@astrojs/starlight/schema';
import { blogSchema } from 'starlight-blog/schema';

// Site-wide "early preview" announcement banner shown on every docs page.
// Starlight's `banner` is per-page frontmatter, so we default it here for the
// whole docs collection (individual pages can still override it).
const EARLY_PREVIEW_BANNER = {
  content:
    '<strong>Early preview.</strong> Maxon is under active development — incomplete in places, with breaking changes expected before a 1.0 release.',
};

export const collections = {
  // Blog posts live under src/content/docs/blog/ and use the blog schema
  // (authors, date, tags, excerpt) layered onto the Starlight docs schema.
  docs: defineCollection({
    loader: docsLoader(),
    schema: docsSchema({
      extend: (context) =>
        blogSchema(context).extend({
          banner: z.object({ content: z.string() }).default(EARLY_PREVIEW_BANNER),
        }),
    }),
  }),
};
