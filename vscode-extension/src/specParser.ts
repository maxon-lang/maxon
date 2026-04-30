import * as fs from 'fs';
import * as path from 'path';

export interface SpecTestMarker {
	name: string;
	line: number;
	column: number;
}

export interface SpecFile {
	filePath: string;
	specName: string;
	feature: string;
	status: string;
	category: string;
	tests: SpecTestMarker[];
}

const FRONTMATTER_RE = /^---\r?\n([\s\S]*?)\r?\n---/;
// `g` flag carries lastIndex state across calls — instantiate fresh per parse instead.
const TEST_MARKER_PATTERN = '<!--\\s*test:\\s*(\\S+)\\s*-->';

function extractYamlValue(yaml: string, key: string): string | undefined {
	const m = new RegExp(`^${key}:\\s*(.+)$`, 'm').exec(yaml);
	return m ? m[1].trim() : undefined;
}

function parseFrontmatter(content: string): { feature: string; status: string; category: string; } {
	const m = FRONTMATTER_RE.exec(content);
	if (!m) return { feature: 'unknown', status: 'unknown', category: 'unknown' };
	const yaml = m[1];
	return {
		feature: extractYamlValue(yaml, 'feature') ?? 'unknown',
		status: extractYamlValue(yaml, 'status') ?? 'unknown',
		category: extractYamlValue(yaml, 'category') ?? 'unknown'
	};
}

function offsetToPosition(content: string, offset: number): { line: number; column: number; } {
	let line = 0;
	let lastNl = -1;
	for (let i = 0; i < offset; i++) {
		if (content.charCodeAt(i) === 10) {
			line++;
			lastNl = i;
		}
	}
	return { line, column: offset - lastNl - 1 };
}

export function parseSpecContent(filePath: string, content: string): SpecFile {
	const { feature, status, category } = parseFrontmatter(content);
	const tests: SpecTestMarker[] = [];
	const markerRe = new RegExp(TEST_MARKER_PATTERN, 'g');
	let match: RegExpExecArray | null;
	while ((match = markerRe.exec(content)) !== null) {
		const pos = offsetToPosition(content, match.index);
		tests.push({ name: match[1], line: pos.line, column: pos.column });
	}
	return {
		filePath,
		specName: path.basename(filePath, '.md'),
		feature,
		status,
		category,
		tests
	};
}

export function parseSpecFile(filePath: string): SpecFile {
	const content = fs.readFileSync(filePath, 'utf8');
	return parseSpecContent(filePath, content);
}

/**
 * Mirrors SpecParser.cs:38-47 — skip status: draft and status: selfhosted.
 * The self-hosted profile re-includes selfhosted specs at run time.
 */
export function shouldIncludeForBootstrap(spec: SpecFile): boolean {
	return spec.status !== 'draft' && spec.status !== 'selfhosted';
}

export function shouldIncludeForSelfHosted(spec: SpecFile): boolean {
	return spec.status !== 'draft';
}

export function parseSpecDirectory(specDir: string): SpecFile[] {
	const out: SpecFile[] = [];
	let entries: string[];
	try {
		entries = fs.readdirSync(specDir);
	} catch {
		return out;
	}
	for (const entry of entries) {
		if (!entry.endsWith('.md')) continue;
		const filePath = path.join(specDir, entry);
		try {
			out.push(parseSpecFile(filePath));
		} catch {
			// ignore unreadable files; the runner will surface them
		}
	}
	return out;
}
