// Generates public/og.png (1200x630) from an inline SVG using sharp.
// Run with: node scripts/make-og.mjs
import sharp from 'sharp';
import { fileURLToPath } from 'node:url';

const svg = `
<svg width="1200" height="630" viewBox="0 0 1200 630" xmlns="http://www.w3.org/2000/svg">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#0e151e"/>
      <stop offset="1" stop-color="#070b10"/>
    </linearGradient>
    <pattern id="grid" width="44" height="44" patternUnits="userSpaceOnUse">
      <path d="M44 0H0V44" fill="none" stroke="#00ADD8" stroke-opacity="0.06" stroke-width="1"/>
    </pattern>
  </defs>
  <rect width="1200" height="630" fill="url(#bg)"/>
  <rect width="1200" height="630" fill="url(#grid)"/>

  <g transform="translate(96, 150)">
    <g transform="scale(4.2)">
      <path d="M8 1L14.5 4.5V11.5L8 15L1.5 11.5V4.5L8 1Z" fill="#00ADD8" fill-opacity="0.10" stroke="#00ADD8" stroke-width="1.1" stroke-linejoin="round"/>
      <path d="M4 11V5L8 9L12 5V11" stroke="#00ADD8" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"/>
    </g>
  </g>

  <text x="186" y="226" font-family="ui-sans-serif, system-ui, Segoe UI, Roboto, sans-serif" font-size="64" font-weight="700" fill="#ffffff">Maxon</text>

  <text x="96" y="392" font-family="ui-sans-serif, system-ui, Segoe UI, Roboto, sans-serif" font-size="82" font-weight="800" fill="#ffffff"><tspan fill="#00ADD8" font-style="italic">You</tspan> Aren't Going</text>
  <text x="96" y="480" font-family="ui-sans-serif, system-ui, Segoe UI, Roboto, sans-serif" font-size="82" font-weight="800" fill="#ffffff">To Write It.</text>

  <text x="96" y="560" font-family="ui-monospace, SFMono-Regular, Menlo, Consolas, monospace" font-size="28" fill="#8aa0b2">Maxon · written by AI, for AI · maxon.dev</text>
</svg>`;

const out = fileURLToPath(new URL('../public/og.png', import.meta.url));
await sharp(Buffer.from(svg)).png().toFile(out);
console.log('wrote', out);
