import * as fs from 'fs';
import * as path from 'path';

// Everything the Test Explorer decides about `maxon test` that does not need VS Code, so it runs under
// plain mocha.

export const TestFileSuffix = '.test.maxon';
const MaxonSourceSuffix = '.maxon';
const IgnoreMarkerName = '.maxonignore';
const DriverFileNames = ['project.maxon', 'tasks.maxon'];

export interface DeclaredTest {
	name: string;
	/** Zero-based, as VS Code positions are. */
	line: number;
	column: number;
}

const TEST_DECLARATION_RE = /^([ \t]*)test[ \t]+'([^'\r\n]*)'/;

enum ScanState {
	Code,
	LineComment,
	BlockComment,
	StringLiteral
}

/**
 * The `test '<name>'` declarations in one file's source.
 *
 * A declaration opens only at the start of a line that begins in code — never inside a block comment —
 * which is where the compiler's contextual keyword opens one. Strings and line comments cannot span a
 * line, so only a block comment carries state across lines.
 */
export function parseTestDeclarations(content: string): DeclaredTest[] {
	const found: DeclaredTest[] = [];
	const seen = new Set<string>();
	let state = ScanState.Code;
	let line = 0;
	let lineStart = 0;

	for (let i = 0; i <= content.length; i++) {
		if (i === lineStart && state === ScanState.Code) {
			const lineEnd = content.indexOf('\n', i);
			const text = content.slice(i, lineEnd === -1 ? content.length : lineEnd);
			const match = TEST_DECLARATION_RE.exec(text);

			// An empty name is E2059, and a repeated one cannot be told apart in a report keyed by name.
			if (match && match[2].length > 0 && !seen.has(match[2])) {
				seen.add(match[2]);
				found.push({ name: match[2], line, column: match[1].length });
			}
		}

		if (i === content.length) break;

		const ch = content[i];
		const next = content[i + 1];

		if (ch === '\n') {
			line++;
			lineStart = i + 1;
			if (state !== ScanState.BlockComment) state = ScanState.Code;
			continue;
		}

		switch (state) {
			case ScanState.Code:
				if (ch === '/' && next === '/') {
					state = ScanState.LineComment;
					i++;
				} else if (ch === '/' && next === '*') {
					state = ScanState.BlockComment;
					i++;
				} else if (ch === '"') {
					state = ScanState.StringLiteral;
				}
				break;
			case ScanState.LineComment:
				break;
			case ScanState.BlockComment:
				if (ch === '*' && next === '/') {
					state = ScanState.Code;
					i++;
				}
				break;
			case ScanState.StringLiteral:
				if (ch === '\\') {
					i++;
				} else if (ch === '"') {
					state = ScanState.Code;
				}
				break;
			default:
				throw new Error(`parseTestDeclarations: unhandled scan state ${state}`);
		}
	}

	return found;
}

/** Is `dir` excluded from every compile — by its own `.maxonignore` or by one in any directory above it? */
export function isIgnoredDirectory(dir: string): boolean {
	let current = path.resolve(dir);

	while (true) {
		if (fs.existsSync(path.join(current, IgnoreMarkerName))) return true;

		const above = path.dirname(current);
		if (above === current) return false;
		current = above;
	}
}

function holdsMaxonSource(dir: string): boolean {
	try {
		return fs.readdirSync(dir, { withFileTypes: true })
			.some(entry => entry.isFile() && !DriverFileNames.includes(entry.name.toLowerCase()) && entry.name.endsWith(MaxonSourceSuffix));
	} catch {
		return false;
	}
}

/**
 * The directory `maxon test` is pointed at for this test file.
 *
 * `maxon test <dir>` compiles every `.maxon` file beneath `<dir>` as one program, and nothing on disk marks
 * where a project begins. The project is therefore the highest directory reachable from the test file's own
 * directory through parents that each hold a `.maxon` file, never above the workspace folder: sources that
 * sit together are compiled together, and a directory holding none (`tests/` above `tests/cli/`) separates
 * independent projects. A `project.maxon` or `tasks.maxon`, in any letter case, is not counted: `maxon test` compiles neither.
 */
export function testProjectDirectory(testFile: string, workspaceFolder: string): string {
	const root = path.resolve(workspaceFolder);
	let project = path.dirname(path.resolve(testFile));

	while (isWithin(project, root)) {
		const above = path.dirname(project);
		if (!holdsMaxonSource(above)) break;
		project = above;
	}

	return project;
}

function isWithin(child: string, parent: string): boolean {
	const relative = path.relative(parent, child);
	return relative !== '' && !relative.startsWith('..') && !path.isAbsolute(relative);
}

const caseInsensitivePaths = process.platform === 'win32' || process.platform === 'darwin';

/** A path as an identity: resolved, and case-folded where the host's filesystem folds case. */
export function pathKey(file: string): string {
	const resolved = path.resolve(file);
	return caseInsensitivePaths ? resolved.toLowerCase() : resolved;
}

/** How `maxon test` spells a file in its report and matches it against `--filter`. */
export function reportPath(file: string, workingDirectory: string): string {
	const relative = path.relative(workingDirectory, file);
	const spelled = relative === '' || relative.startsWith('..') || path.isAbsolute(relative) ? file : relative;
	return spelled.split(path.sep).join('/');
}

export interface FilterSelection {
	/** Files whose every test is requested. */
	wholeFiles: string[];
	/** Tests requested from files not requested whole. */
	testNames: string[];
}

/**
 * The `--filter` value selecting at least the requested tests, or undefined for the whole project.
 *
 * The compiler matches each comma-separated pattern as a case-insensitive substring of a test's name or its
 * reported path. A name holding a comma is split into parts that each still match it, so the selection is a
 * superset of what was asked; results are matched back by file and name, never by the filter.
 */
export function testFilterFor(selection: FilterSelection, workingDirectory: string, wholeProject: boolean): string | undefined {
	if (wholeProject) return undefined;

	const patterns = [
		...selection.wholeFiles.map(file => reportPath(file, workingDirectory)),
		...selection.testNames
	];
	if (patterns.length === 0) {
		throw new Error('testFilterFor: a run of part of a project selected no test');
	}
	return patterns.join(',');
}

// ---------------------------------------------------------------------------------------------------------
// The `--json` report
// ---------------------------------------------------------------------------------------------------------

/** `maxon test`'s exit codes. Any other code means no report was printed. */
export enum TestProjectExitCode {
	AllPassed = 0,
	/** A test did not pass, or no test was found — the report says which. */
	TestsFailed = 1
}

export type TestState = 'passed' | 'failed' | 'crashed' | 'timedOut' | 'didNotRun' | 'leaked';

export interface ThrownError {
	errorType: string;
	errorCase: string;
	file: string;
	line: number;
}

export interface TestResult {
	file: string;
	name: string;
	symbol: string;
	line: number;
	state: TestState;
	nanos?: number;
	output?: string;
	threw?: ThrownError;
}

export interface TestRunDocument {
	reason?: string;
	total: number;
	passed: number;
	failed: number;
	files: number;
	results: TestResult[];
}

const KNOWN_STATES: ReadonlySet<string> = new Set<TestState>(['passed', 'failed', 'crashed', 'timedOut', 'didNotRun', 'leaked']);

/** The one JSON document `maxon test --json` prints on stdout. Throws on anything else. */
export function parseTestRunDocument(stdout: string): TestRunDocument {
	const parsed: unknown = JSON.parse(stdout.trim());
	if (!isObject(parsed) || !Array.isArray(parsed.results)) {
		throw new Error('maxon test --json printed JSON without a "results" array');
	}

	for (const result of parsed.results) {
		if (!isObject(result) || typeof result.file !== 'string' || typeof result.name !== 'string'
			|| typeof result.state !== 'string') {
			throw new Error(`maxon test --json printed a result without file, name and state: ${JSON.stringify(result)}`);
		}
		if (!KNOWN_STATES.has(result.state)) {
			throw new Error(`maxon test --json reported the unknown state '${result.state}' for '${result.name}'`);
		}
	}

	return parsed as unknown as TestRunDocument;
}

function isObject(value: unknown): value is Record<string, unknown> {
	return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export interface SourceLocation {
	file: string;
	/** Zero-based. */
	line: number;
}

export type TestVerdict =
	| { kind: 'passed'; durationMs?: number; }
	| { kind: 'failed' | 'errored'; message: string; durationMs?: number; location?: SourceLocation; };

const NANOS_PER_MILLISECOND = 1_000_000;

// `FAIL <file>:<line>: <assertion>` — the first line of a failed assertion's output, naming the assertion's
// own line by the file's base name.
const ASSERTION_LINE_RE = /^FAIL (.+):(\d+): /;

const STATE_PREAMBLE: Record<Exclude<TestState, 'passed' | 'failed'>, string> = {
	crashed: 'Crashed: the test took its process down before it finished.',
	timedOut: 'Timed out: the test was still running when its process reached the timeout, and was killed.',
	didNotRun: 'Did not run: the process this test was selected for died before reaching it.',
	leaked: 'Leaked: the test held an allocation at exit.'
};

/**
 * What VS Code is told about one result. `testFile` is the absolute path of the file declaring the test,
 * and `workingDirectory` is where `maxon test` ran, which a relative path in the report is relative to.
 */
export function verdictFor(result: TestResult, testFile: string, workingDirectory: string): TestVerdict {
	const durationMs = result.nanos === undefined ? undefined : result.nanos / NANOS_PER_MILLISECOND;

	if (result.state === 'passed') {
		return { kind: 'passed', durationMs };
	}

	const details: string[] = [];
	if (result.state !== 'failed') details.push(STATE_PREAMBLE[result.state]);
	if (result.threw) {
		details.push(`Threw ${result.threw.errorType}.${result.threw.errorCase} at ${result.threw.file}:${result.threw.line}`);
	}
	if (result.output) details.push(result.output);
	if (details.length === 0) details.push('Test failed');

	return {
		kind: result.state === 'didNotRun' ? 'errored' : 'failed',
		message: details.join('\n'),
		durationMs,
		location: failureLocation(result, testFile, workingDirectory)
	};
}

function failureLocation(result: TestResult, testFile: string, workingDirectory: string): SourceLocation | undefined {
	if (result.threw) {
		const thrownFile = path.resolve(workingDirectory, result.threw.file);
		if (fs.existsSync(thrownFile)) return { file: thrownFile, line: result.threw.line - 1 };
	}

	const assertion = ASSERTION_LINE_RE.exec(result.output ?? '');
	if (assertion && path.basename(assertion[1]) === path.basename(testFile)) {
		return { file: testFile, line: Number(assertion[2]) - 1 };
	}

	return undefined;
}

/** The identity a result and a discovered test share: the declaring file and the test's name. */
export function testKey(file: string, name: string): string {
	return JSON.stringify([pathKey(file), name]);
}

/** The key of a reported result, whose `file` is relative to where `maxon test` ran unless it climbed out. */
export function resultKey(result: TestResult, workingDirectory: string): string {
	return testKey(path.resolve(workingDirectory, result.file), result.name);
}
