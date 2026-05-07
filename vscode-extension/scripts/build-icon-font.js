// Builds a single-glyph WOFF font (`maxon-icons.woff`) used for the
// `maxon-logo` codicon. Uses opentype.js so we get direct control over
// contour winding direction — TrueType uses the nonzero fill rule, so the
// outer contour and the inner "hole" contour must wind opposite directions
// or the hole won't cut.
//
// Run with:  node scripts/build-icon-font.js
//
// The .woff is committed; this script only needs to run when the glyph
// design changes.

const fs = require('fs');
const path = require('path');
const opentype = require('opentype.js');

// Glyph design grid: 1000 units. Authored in top-left coords, then flipped
// to font coords (origin bottom-left) at emission time.
const UNITS_PER_EM = 1000;

// Each contour is a list of [x, y] points in top-left coordinates. The
// `cw` flag describes the *visual* winding (in top-left coords). Y-flipping
// to font coords reverses the visual direction, so during emission we
// reverse points when needed to land at the TrueType convention:
// outer contours CW in font coords, hole contours CCW in font coords.
const CONTOURS = [
	{
		// Outer hexagon — visually clockwise in top-left coords.
		cw: true,
		points: [
			[500, 30], [920, 270], [920, 730],
			[500, 970], [80, 730], [80, 270],
		],
	},
	{
		// Inner hexagon — same visual CW direction, declared as a hole.
		// We'll reverse it at emission so it ends up CCW in font coords.
		cw: false,
		points: [
			[500, 130], [833, 322], [833, 678],
			[500, 870], [167, 678], [167, 322],
		],
	},
	{
		// Filled "M" — visually clockwise outer outline.
		cw: true,
		points: [
			[250, 690], [250, 310], [350, 310],
			[500, 540], [650, 310], [750, 310],
			[750, 690], [670, 690], [670, 450],
			[540, 650], [460, 650], [330, 450],
			[330, 690],
		],
	},
];

function buildPath() {
	const p = new opentype.Path();
	for (const contour of CONTOURS) {
		// Flip Y to font coords (origin bottom-left). The flip reverses
		// visual winding: a contour that was CW in top-left coords ends
		// up CCW in font coords, and vice versa.
		//
		// PostScript convention: outer = CW, hole = CCW (in font coords).
		// TrueType convention: outer = CCW, hole = CW (in font coords).
		// Both rasterizers (Chromium/Skia included) honor TrueType
		// convention for .ttf/.woff. So we want outer CCW in font coords.
		// After Y-flip, a top-left CW contour is already CCW in font
		// coords — so reverse outer contours, leave holes alone.
		let pts = contour.points.map(([x, y]) => [x, UNITS_PER_EM - y]);
		if (contour.cw) pts = pts.slice().reverse();

		const [x0, y0] = pts[0];
		p.moveTo(x0, y0);
		for (let i = 1; i < pts.length; i++) {
			p.lineTo(pts[i][0], pts[i][1]);
		}
		p.close();
	}
	return p;
}

const notdef = new opentype.Glyph({
	name: '.notdef',
	unicode: 0,
	advanceWidth: UNITS_PER_EM,
	path: new opentype.Path(),
});

const logo = new opentype.Glyph({
	name: 'maxon-logo',
	unicode: 0xE000,
	advanceWidth: UNITS_PER_EM,
	path: buildPath(),
});

const font = new opentype.Font({
	familyName: 'maxon-icons',
	styleName: 'Regular',
	unitsPerEm: UNITS_PER_EM,
	ascender: UNITS_PER_EM,
	descender: 0,
	glyphs: [notdef, logo],
});

const ttfBuffer = Buffer.from(font.toArrayBuffer());

// Convert TTF → WOFF.
const ttf2woff = require('ttf2woff');
const woffBuffer = Buffer.from(ttf2woff(ttfBuffer).buffer);

const outDir = path.join(__dirname, '..', 'icons');
fs.mkdirSync(outDir, { recursive: true });
const outPath = path.join(outDir, 'maxon-icons.woff');
fs.writeFileSync(outPath, woffBuffer);
console.log(`Wrote ${outPath} (${woffBuffer.length} bytes)`);
