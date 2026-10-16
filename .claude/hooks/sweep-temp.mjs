import { readdirSync, rmSync, statSync } from "node:fs";
import { join } from "node:path";

const maxAgeMs = 3 * 24 * 60 * 60 * 1000;
const kept = new Set([".maxonignore"]);
const tempDir = join(process.env.CLAUDE_PROJECT_DIR ?? process.cwd(), "temp");
const cutoff = Date.now() - maxAgeMs;

function touchedSince(path) {
	const stat = statSync(path);

	if (stat.mtimeMs >= cutoff) {
		return true;
	}

	if (!stat.isDirectory()) {
		return false;
	}

	return readdirSync(path).some((name) => touchedSince(join(path, name)));
}

let entries;

try {
	entries = readdirSync(tempDir);
} catch (error) {
	if (error.code === "ENOENT") {
		process.exit(0);
	}

	throw error;
}

for (const name of entries) {
	if (kept.has(name)) {
		continue;
	}

	const path = join(tempDir, name);

	try {
		if (!touchedSince(path)) {
			rmSync(path, { recursive: true, force: true });
		}
	} catch (error) {
		process.stderr.write(`sweep-temp: left ${path}: ${error.message}\n`);
	}
}
