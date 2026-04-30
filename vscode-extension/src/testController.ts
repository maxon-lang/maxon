import { spawn, ChildProcessWithoutNullStreams } from 'child_process';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import * as vscode from 'vscode';
import { log } from './logger';
import {
	parseSpecContent,
	parseSpecDirectory,
	SpecFile,
	SpecTestMarker,
	shouldIncludeForBootstrap,
	shouldIncludeForSelfHosted
} from './specParser';

const isWindows = os.platform() === 'win32';
const sharpBinaryName = isWindows ? 'maxon.exe' : 'maxon';
const selfHostedBinaryName = isWindows ? 'maxon-selfhosted.exe' : 'maxon-selfhosted';

interface ProfileBinding {
	profile: vscode.TestRunProfile;
	binary: string;
	cwd: string;
	includes(spec: SpecFile): boolean;
}

const TEST_LINE_RE = /^\s*(?:\[W\d+\]\s+)?\[(PASS|FAIL)\]\s+(\S+)(?:\s+\((\d+)ms\))?\s*$/;
// The C# runner reports failures as "specs/fragments/..." (TestRunner.cs:136
// hard-codes the generic prefix). The self-hosted runner reports them as
// "specs/fragments-{target}/..." (SpecTestRunner.maxon:242). Accept both.
const FAILURE_PATH_RE = /^specs\/fragments(?:-[^/]+)?\/([^/]+)\/(.+)\.test$/;
const SPEC_FAIL_RE = /^\s*\[FAIL\]\s+\S+\s+\(\d+\/\d+\)\s*$/;
const SUMMARY_RE = /^Tests:\s+\d+\s+passed/;
// Both runners prefix every line with a category code:
//   C# (Logger.cs:89,98):           "[TST] msg"  (Info)  /  "[TST] ERROR: msg"  (Error)
//   Self-hosted (Logger.maxon:178): "[TST] INFO: msg"     /  "[TST] ERROR: msg"
// Strip the prefix so the patterns above can match the inner payload.
const LOG_PREFIX_RE = /^\[[A-Z]{2,4}\]\s+(?:(?:ERROR|INFO|DEBUG|TRACE):\s+)?/;
const ERROR_LINE_RE = /^\[[A-Z]{2,4}\]\s+ERROR:/;

export function registerTestController(): vscode.Disposable {
	const folders = vscode.workspace.workspaceFolders;
	log(`Test controller activating; workspaceFolders: ${
		folders ? folders.map(f => f.uri.fsPath).join(', ') : '(none)'
	}`);
	if (!folders || folders.length === 0) {
		log('No workspace folder open — test controller not registered');
		return new vscode.Disposable(() => { /* no-op */ });
	}
	const workspaceRoot = folders[0].uri.fsPath;
	const specDir = path.join(workspaceRoot, 'specs');
	const specDirExists = fs.existsSync(specDir);
	log(`Looking for specs at ${specDir} (exists: ${specDirExists})`);

	const controller = vscode.tests.createTestController('maxonSpecTests', 'Maxon Spec Tests');

	if (!specDirExists) {
		log('No specs/ directory; controller registered with empty tree');
	}

	const specItems = new Map<string, vscode.TestItem>();
	const testItemById = new Map<string, vscode.TestItem>();
	const specByName = new Map<string, SpecFile>();

	const sharpBinary = path.join(workspaceRoot, 'bin', sharpBinaryName);
	const selfHostedBinary = path.join(
		workspaceRoot,
		'maxon-selfhosted',
		'.maxon',
		selfHostedBinaryName
	);

	const bindings: ProfileBinding[] = [
		{
			profile: controller.createRunProfile(
				'C# Bootstrap (maxon-sharp)',
				vscode.TestRunProfileKind.Run,
				(req, tok) => runHandler(req, tok, bindingFor('sharp')),
				true
			),
			binary: sharpBinary,
			cwd: workspaceRoot,
			includes: shouldIncludeForBootstrap
		},
		{
			profile: controller.createRunProfile(
				'Self-Hosted (maxon-selfhosted)',
				vscode.TestRunProfileKind.Run,
				(req, tok) => runHandler(req, tok, bindingFor('selfhosted')),
				false
			),
			binary: selfHostedBinary,
			cwd: workspaceRoot,
			includes: shouldIncludeForSelfHosted
		}
	];

	function bindingFor(kind: 'sharp' | 'selfhosted'): ProfileBinding {
		return kind === 'sharp' ? bindings[0] : bindings[1];
	}

	// Sync a single spec's tree node and child test items. Returns the count of
	// child tests so the caller can produce a discovery summary.
	function syncSpec(spec: SpecFile): number {
		specByName.set(spec.specName, spec);
		const specUri = vscode.Uri.file(spec.filePath);

		let specItem = specItems.get(spec.specName);
		if (!specItem) {
			specItem = controller.createTestItem(spec.specName, spec.specName, specUri);
			controller.items.add(specItem);
			specItems.set(spec.specName, specItem);
		}
		specItem.description = spec.feature;

		const childIds = new Set<string>();
		for (const test of spec.tests) {
			const id = `${spec.specName}/${test.name}`;
			childIds.add(id);
			let item = testItemById.get(id);
			if (!item) {
				item = controller.createTestItem(id, test.name, specUri);
				testItemById.set(id, item);
				specItem.children.add(item);
			}
			item.range = markerRange(test);
		}
		for (const child of childList(specItem)) {
			if (!childIds.has(child.id)) {
				specItem.children.delete(child.id);
				testItemById.delete(child.id);
			}
		}
		return spec.tests.length;
	}

	function refreshFromDisk(): void {
		const specs = parseSpecDirectory(specDir);
		const seenSpecs = new Set<string>();
		let testCount = 0;

		for (const spec of specs) {
			seenSpecs.add(spec.specName);
			testCount += syncSpec(spec);
		}

		for (const [name, item] of [...specItems]) {
			if (!seenSpecs.has(name)) {
				controller.items.delete(item.id);
				specItems.delete(name);
				specByName.delete(name);
			}
		}
		log(`Discovered ${testCount} spec test(s) across ${seenSpecs.size} spec file(s)`);
	}

	refreshFromDisk();
	controller.refreshHandler = async () => { refreshFromDisk(); };

	const watcher = vscode.workspace.createFileSystemWatcher(
		new vscode.RelativePattern(specDir, '*.md')
	);
	watcher.onDidChange(uri => refreshSingle(uri));
	watcher.onDidCreate(uri => refreshSingle(uri));
	watcher.onDidDelete(uri => removeSingle(uri));

	function refreshSingle(uri: vscode.Uri): void {
		try {
			const content = fs.readFileSync(uri.fsPath, 'utf8');
			syncSpec(parseSpecContent(uri.fsPath, content));
		} catch (err) {
			log(`Failed to refresh ${uri.fsPath}: ${err}`);
		}
	}

	function removeSingle(uri: vscode.Uri): void {
		const specName = path.basename(uri.fsPath, '.md');
		const item = specItems.get(specName);
		if (!item) return;
		for (const child of childList(item)) testItemById.delete(child.id);
		controller.items.delete(item.id);
		specItems.delete(specName);
		specByName.delete(specName);
	}

	async function runHandler(
		request: vscode.TestRunRequest,
		token: vscode.CancellationToken,
		binding: ProfileBinding
	): Promise<void> {
		const run = controller.createTestRun(request);

		if (!fs.existsSync(binding.binary)) {
			const msg = `Compiler binary not found: ${binding.binary}`;
			log(msg);
			vscode.window.showErrorMessage(msg);
			run.end();
			return;
		}

		const requested = collectRequested(
			request, controller, testItemById, specItems, specByName, binding
		);
		for (const item of requested.skipped) run.skipped(item);
		if (requested.items.length === 0) {
			run.end();
			return;
		}

		for (const item of requested.items) run.enqueued(item);

		const filters = buildFilters(requested, specByName, binding);
		try {
			for (const filter of filters) {
				if (token.isCancellationRequested) break;
				await runWithFilter(binding, filter, requested, run, token);
			}
		} catch (err) {
			log(`Test run failed: ${err}`);
		} finally {
			run.end();
		}
	}

	return new vscode.Disposable(() => {
		watcher.dispose();
		controller.dispose();
	});
}

function markerRange(test: SpecTestMarker): vscode.Range {
	const pos = new vscode.Position(test.line, test.column);
	return new vscode.Range(pos, pos);
}

function childList(item: vscode.TestItem): vscode.TestItem[] {
	const out: vscode.TestItem[] = [];
	item.children.forEach(c => out.push(c));
	return out;
}

interface RequestedTests {
	items: vscode.TestItem[];
	itemById: Map<string, vscode.TestItem>;
	bySpec: Map<string, { all: boolean; tests: vscode.TestItem[]; }>;
	// Items whose spec is excluded by the active profile (e.g. status: selfhosted
	// when running the C# bootstrap). The runner won't see them, so we mark them
	// skipped explicitly instead of leaving them unresolved.
	skipped: vscode.TestItem[];
}

function collectRequested(
	request: vscode.TestRunRequest,
	controller: vscode.TestController,
	testItemById: Map<string, vscode.TestItem>,
	specItems: Map<string, vscode.TestItem>,
	specByName: Map<string, SpecFile>,
	binding: ProfileBinding
): RequestedTests {
	const exclude = new Set<string>();
	for (const ex of request.exclude ?? []) exclude.add(ex.id);

	const items: vscode.TestItem[] = [];
	const itemById = new Map<string, vscode.TestItem>();
	const skipped: vscode.TestItem[] = [];
	const bySpec = new Map<string, { all: boolean; tests: vscode.TestItem[]; }>();

	const seedItems: vscode.TestItem[] = [];
	if (request.include) {
		seedItems.push(...request.include);
	} else {
		controller.items.forEach(i => seedItems.push(i));
	}

	for (const seed of seedItems) {
		if (exclude.has(seed.id)) continue;
		if (specItems.has(seed.id)) {
			// Whole-spec selection
			const specName = seed.id;
			const spec = specByName.get(specName);
			const tests: vscode.TestItem[] = [];
			seed.children.forEach(child => {
				if (!exclude.has(child.id)) tests.push(child);
			});
			if (tests.length === 0) continue;
			if (spec && !binding.includes(spec)) {
				// Profile excludes this spec (e.g. selfhosted on the C# runner) —
				// the runner won't report on these tests, so mark them skipped here.
				skipped.push(...tests);
				continue;
			}
			items.push(...tests);
			for (const t of tests) itemById.set(t.id, t);
			bySpec.set(specName, { all: tests.length === seed.children.size, tests });
		} else if (testItemById.has(seed.id)) {
			const slash = seed.id.indexOf('/');
			if (slash < 0) continue;
			const specName = seed.id.slice(0, slash);
			const spec = specByName.get(specName);
			if (spec && !binding.includes(spec)) {
				skipped.push(seed);
				continue;
			}
			const entry = bySpec.get(specName) ?? { all: false, tests: [] };
			entry.tests.push(seed);
			bySpec.set(specName, entry);
			items.push(seed);
			itemById.set(seed.id, seed);
		}
	}

	return { items, itemById, bySpec, skipped };
}

/**
 * Build the list of `--filter` values to pass. `null` in the list means
 * "no filter" — the runner walks every spec itself in a single shared
 * worker pool, which is dramatically faster than spawning per-spec.
 */
function buildFilters(
	requested: RequestedTests,
	specByName: Map<string, SpecFile>,
	binding: ProfileBinding
): (string | null)[] {
	// If we've requested whole-spec runs of every spec the profile would
	// include, drop the filter and let the runner do its thing in one process.
	let coversAllEligible = true;
	let eligibleCount = 0;
	for (const spec of specByName.values()) {
		if (!binding.includes(spec)) continue;
		eligibleCount++;
		const entry = requested.bySpec.get(spec.specName);
		if (!entry || !entry.all) {
			coversAllEligible = false;
			break;
		}
	}
	if (coversAllEligible && eligibleCount > 0 && eligibleCount === requested.bySpec.size) {
		return [null];
	}

	const out: (string | null)[] = [];
	for (const [specName, entry] of requested.bySpec) {
		if (entry.all) {
			out.push(`${specName}/`);
		} else {
			for (const test of entry.tests) out.push(test.id);
		}
	}
	return out;
}

interface PendingFailure {
	item: vscode.TestItem;
	durationMs?: number;
	detail: string[];
}

interface ActiveRun {
	requested: RequestedTests;
	run: vscode.TestRun;
	// Failures wait until end-of-process: the per-test verbose [FAIL] line
	// arrives first (no detail), and the per-spec failure block follows with
	// the actual error message. We pair them up at flush time.
	pendingFailures: Map<string, PendingFailure>;
	failureDetailFor?: string;
}

async function runWithFilter(
	binding: ProfileBinding,
	filter: string | null,
	requested: RequestedTests,
	run: vscode.TestRun,
	token: vscode.CancellationToken
): Promise<void> {
	const args = ['spec-test', '--verbose'];
	if (filter !== null) args.splice(1, 0, `--filter=${filter}`);
	log(`Spawning ${binding.binary} ${args.join(' ')}`);
	run.appendOutput(`> ${binding.binary} ${args.join(' ')}\r\n`);

	let child: ChildProcessWithoutNullStreams;
	try {
		child = spawn(binding.binary, args, { cwd: binding.cwd });
	} catch (err) {
		log(`Failed to spawn: ${err}`);
		failAllInScope(run, requested, filter, `Failed to spawn compiler: ${err}`);
		return;
	}

	const cancelSub = token.onCancellationRequested(() => {
		try { child.kill(); } catch { /* ignore */ }
	});

	const active: ActiveRun = {
		requested,
		run,
		pendingFailures: new Map()
	};

	const lineHandler = makeLineHandler(active);
	pipeLines(child.stdout, lineHandler);
	pipeLines(child.stderr, lineHandler);

	await new Promise<void>(resolve => {
		child.on('close', code => {
			cancelSub.dispose();
			flushPendingDetails(active);
			if (code !== 0 && code !== null && !token.isCancellationRequested) {
				run.appendOutput(`\r\nProcess exited with code ${code}\r\n`);
			}
			resolve();
		});
		child.on('error', err => {
			cancelSub.dispose();
			log(`Process error: ${err}`);
			failAllInScope(run, requested, filter, `Process error: ${err}`);
			resolve();
		});
	});
}

function makeLineHandler(active: ActiveRun) {
	const { requested, run } = active;
	return (line: string) => {
		run.appendOutput(line.replace(/\r?\n?$/, '') + '\r\n');

		const raw = line.trimEnd();
		const trimmed = raw.replace(LOG_PREFIX_RE, '');
		const isErrorLine = ERROR_LINE_RE.test(raw);

		const m = TEST_LINE_RE.exec(trimmed);
		if (m) {
			const status = m[1];
			const id = m[2];
			const ms = m[3] ? parseInt(m[3], 10) : undefined;
			const item = requested.itemById.get(id);
			if (item) {
				if (status === 'PASS') {
					run.passed(item, ms);
				} else {
					// Defer reporting: the failure detail arrives later in
					// the per-spec failure block.
					active.pendingFailures.set(id, { item, durationMs: ms, detail: [] });
				}
			}
			active.failureDetailFor = undefined;
			return;
		}

		if (SPEC_FAIL_RE.test(trimmed) || SUMMARY_RE.test(trimmed)) {
			active.failureDetailFor = undefined;
			return;
		}

		const fp = FAILURE_PATH_RE.exec(trimmed);
		if (fp) {
			const id = `${fp[1]}/${fp[2]}`;
			active.failureDetailFor = id;
			if (!active.pendingFailures.has(id)) {
				const item = requested.itemById.get(id);
				if (item) {
					// Per-spec block named a test we never saw a [FAIL] line for
					// (e.g. compile error before per-test reporting). Treat it as
					// a failure so it doesn't end up "skipped".
					active.pendingFailures.set(id, { item, detail: [] });
				}
			}
			return;
		}

		// Anything else, while we're inside a failure detail block, is part
		// of the message — but only ERROR-level lines, since the runner
		// interleaves info lines like "Total compile time" and "Generated
		// N fragment(s)" inside the failure block. We can't rely on indent
		// because LOG_PREFIX_RE has already swallowed the leading
		// whitespace from "[TST] ERROR:   detail".
		if (active.failureDetailFor && isErrorLine && trimmed.length > 0) {
			const pf = active.pendingFailures.get(active.failureDetailFor);
			if (pf) pf.detail.push(trimmed);
		}
	};
}

function flushPendingDetails(active: ActiveRun) {
	for (const [, pf] of active.pendingFailures) {
		const message = new vscode.TestMessage(
			pf.detail.length > 0 ? pf.detail.join('\n') : 'Test failed'
		);
		if (pf.item.uri && pf.item.range) {
			message.location = new vscode.Location(pf.item.uri, pf.item.range);
		}
		active.run.failed(pf.item, message, pf.durationMs);
	}
	active.pendingFailures.clear();
}

function pipeLines(
	stream: NodeJS.ReadableStream,
	onLine: (line: string) => void
): void {
	let buffer = '';
	stream.setEncoding('utf8');
	stream.on('data', (chunk: string) => {
		buffer += chunk;
		let nl: number;
		while ((nl = buffer.indexOf('\n')) !== -1) {
			const line = buffer.slice(0, nl);
			buffer = buffer.slice(nl + 1);
			onLine(line);
		}
	});
	stream.on('end', () => {
		if (buffer.length > 0) onLine(buffer);
	});
}

function failAllInScope(
	run: vscode.TestRun,
	requested: RequestedTests,
	filter: string | null,
	message: string
): void {
	for (const item of requested.items) {
		if (!itemMatchesFilter(item, filter)) continue;
		run.errored(item, new vscode.TestMessage(message));
	}
}

function itemMatchesFilter(item: vscode.TestItem, filter: string | null): boolean {
	if (filter === null) return true;            // no-filter run: every requested item is in scope
	if (filter.endsWith('/')) return item.id.startsWith(filter);
	return item.id === filter;
}
