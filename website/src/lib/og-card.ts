/**
 * Build-time Open Graph cards for the docs.
 *
 * Starlight emits `twitter:card: summary_large_image` but no `og:image`, so every
 * shared docs link rendered as a blank card. This draws one PNG per docs page —
 * same dark grid and diamond-M as the site-wide card in scripts/make-og.mjs, with
 * the page's own title on it.
 *
 * SVG is rasterised by sharp at build time, so nothing here runs in the browser.
 */
import sharp from 'sharp';

const WIDTH = 1200;
const HEIGHT = 630;

const SANS = 'ui-sans-serif, system-ui, Segoe UI, Roboto, sans-serif';
const MONO = 'ui-monospace, SFMono-Regular, Menlo, Consolas, monospace';

/** Sidebar group each top-level docs directory belongs to, used as the card's eyebrow. */
const SECTIONS: Record<string, string> = {
  'getting-started': 'Getting Started',
  language: 'Language Reference',
  stdlib: 'Standard Library',
  cli: 'CLI',
  'best-practices': 'Best Practices',
  spec: 'Specification',
  blog: 'Blog',
};

function escapeXml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&apos;');
}

/**
 * Greedy wrap on a character budget. The card is drawn, not laid out, so there is no
 * text metrics engine here — the budget is tuned to the font size actually used and
 * errs narrow, since a title that wraps early still looks composed and one that
 * overflows the canvas does not.
 */
function wrap(text: string, maxChars: number, maxLines: number): string[] {
  const lines: string[] = [];
  let line = '';

  for (const word of text.split(/\s+/).filter(Boolean)) {
    const candidate = line ? `${line} ${word}` : word;
    if (candidate.length <= maxChars || !line) {
      line = candidate;
    } else {
      lines.push(line);
      line = word;
    }
  }
  if (line) lines.push(line);

  if (lines.length <= maxLines) return lines;
  // Too long to fit: keep the leading lines and mark the truncation on the last one.
  const kept = lines.slice(0, maxLines);
  kept[maxLines - 1] = `${kept[maxLines - 1]!.replace(/[\s.,;:]+$/, '')}…`;
  return kept;
}

/** Human-readable section label for a docs entry id such as `docs/language/functions`. */
export function sectionFor(id: string): string {
  const segments = id.split('/');
  // Docs live at src/content/docs/docs/**, so ids carry a leading `docs/` that is
  // routing, not a section. Blog posts sit at `blog/**` with no such prefix.
  const key = segments[0] === 'docs' ? segments[1] : segments[0];
  if (!key) return 'Documentation';
  return SECTIONS[key] ?? key.replace(/-/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase());
}

export function ogSvg({ title, section }: { title: string; section: string }): string {
  // Longer titles get a smaller face so they keep filling the same block of the card
  // rather than shrinking away from it.
  const size = title.length > 44 ? 62 : title.length > 26 ? 72 : 82;
  const budget = size >= 82 ? 20 : size >= 72 ? 24 : 28;
  const lines = wrap(title, budget, 3);

  // Bottom-align the title block so cards with one, two or three lines share a baseline.
  const lineHeight = Math.round(size * 1.12);
  const firstBaseline = 480 - (lines.length - 1) * lineHeight;

  const titleTspans = lines
    .map(
      (line, i) =>
        `<tspan x="96" y="${firstBaseline + i * lineHeight}">${escapeXml(line)}</tspan>`,
    )
    .join('');

  return `<svg width="${WIDTH}" height="${HEIGHT}" viewBox="0 0 ${WIDTH} ${HEIGHT}" xmlns="http://www.w3.org/2000/svg">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#0e151e"/>
      <stop offset="1" stop-color="#070b10"/>
    </linearGradient>
    <pattern id="grid" width="44" height="44" patternUnits="userSpaceOnUse">
      <path d="M44 0H0V44" fill="none" stroke="#00ADD8" stroke-opacity="0.06" stroke-width="1"/>
    </pattern>
  </defs>
  <rect width="${WIDTH}" height="${HEIGHT}" fill="url(#bg)"/>
  <rect width="${WIDTH}" height="${HEIGHT}" fill="url(#grid)"/>

  <g transform="translate(96, 84)">
    <g transform="scale(2.6)">
      <path d="M8 1L14.5 4.5V11.5L8 15L1.5 11.5V4.5L8 1Z" fill="#00ADD8" fill-opacity="0.10" stroke="#00ADD8" stroke-width="1.1" stroke-linejoin="round"/>
      <path d="M4 11V5L8 9L12 5V11" stroke="#00ADD8" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"/>
    </g>
  </g>
  <text x="156" y="118" font-family="${SANS}" font-size="34" font-weight="700" fill="#ffffff">Maxon</text>

  <text x="96" y="238" font-family="${MONO}" font-size="26" fill="#00ADD8" letter-spacing="2">${escapeXml(section.toUpperCase())}</text>

  <text font-family="${SANS}" font-size="${size}" font-weight="800" fill="#ffffff">${titleTspans}</text>

  <text x="96" y="560" font-family="${MONO}" font-size="26" fill="#8aa0b2">maxon.dev · written by AI, for AI</text>
</svg>`;
}

export async function ogPng(opts: { title: string; section: string }): Promise<Buffer> {
  return sharp(Buffer.from(ogSvg(opts))).png().toBuffer();
}
