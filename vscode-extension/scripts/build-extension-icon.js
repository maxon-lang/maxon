// Generates the 256x256 PNG used as the extension icon (the one shown in the
// VS Code Extensions sidebar and on the marketplace). VS Code rejects SVGs
// for that field, so we rasterize maxon-icon.svg here.
//
// Run with:  node scripts/build-extension-icon.js

const fs = require('fs');
const path = require('path');
const sharp = require('sharp');

const svgPath = path.join(__dirname, '..', 'maxon-icon.svg');
const outPath = path.join(__dirname, '..', 'icons', 'maxon-extension-icon.png');

sharp(fs.readFileSync(svgPath), { density: 1024 })
	.resize(256, 256)
	.png()
	.toFile(outPath)
	.then(info => console.log(`Wrote ${outPath} (${info.width}x${info.height}, ${info.size} bytes)`))
	.catch(err => {
		console.error(err);
		process.exit(1);
	});
