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
	SpecTestMarker
} from './specParser';
import { appendRunOutput, childList, pipeLines } from './testItems';

const isWindows = os.platform() === 'win32';
const compilerBinaryName = isWindows ? 'maxon.exe' : 'maxon';

interface ProfileBinding {
	profile: vscode.TestRunProfile;
	binary: string;
	cwd: string;
}

// `spec-test` prints one verdict line per selected test on stdout — `PASS <spec>/<test>`,
// `FAIL <spec>/<test>: <reason>`, `SKIP <spec>/<test>` or `NOTRUN <spec>/<test>` (`verdictLine` in
// maxon-bin/Main.maxon) — then `<n> passed, <m> failed`. A failure's reason continues on the lines
// after its verdict until the next verdict or the summary, and may be empty on the verdict line itself.
const PASS_LINE_RE = /^PASS (\S+)$/;
const FAIL_LINE_RE = /^FAIL (\S+):(?: (.*))?$/;
const NOT_RUN_LINE_RE = /^(?:SKIP|NOTRUN) (\S+)$/;
const SUMMARY_RE = /^\d+ passed, \d+ failed$/;

/** True when `workspaceRoot` is the Maxon compiler checkout, the one place `spec-test` means anything. */
export function isMaxonCheckout(workspaceRoot: string): boolean {
	return isDirectory(path.join(workspaceRoot, 'specs')) && isDirectory(path.join(workspaceRoot, 'maxon-bin'));
}

function isDirectory(candidate: string): boolean {
	try {
		return fs.statSync(candidate).isDirectory();
	} catch {
		return false;
	}
}

/** The checkout's language spec suite, run by the tree's own compiler build. */
export function registerSpecTestController(workspaceRoot: string): vscode.Disposable {
	const specDir = path.join(workspaceRoot, 'specs');
	const controller = vscode.tests.createTestController('maxonSpecTests', 'Maxon Spec Suite');

	const specItems = new Map<string, vscode.TestItem>();
	const testItemById = new Map<string, vscode.TestItem>();
	const specByName = new Map<string, SpecFile>();

	const compilerBinary = path.join(workspaceRoot, 'maxon-bin', '.maxon', compilerBinaryName);

	const binding: ProfileBinding = {
		profile: controller.createRunProfile(
			'Maxon Compiler',
			vscode.TestRunProfileKind.Run,
			(req, tok) => runHandler(req, tok, binding),
			true
		),
		binary: compilerBinary,
		cwd: workspaceRoot
	};

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

		const requested = collectRequested(request, controller, testItemById, specItems);
		if (requested.items.length === 0) {
			run.end();
			return;
		}

		for (const item of requested.items) run.enqueued(item);

		const filters = buildFilters(requested, specByName);
		try {
			await runWithFilters(binding, filters, requested, run, token);
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

interface RequestedTests {
	items: vscode.TestItem[];
	itemById: Map<string, vscode.TestItem>;
	bySpec: Map<string, { all: boolean; tests: vscode.TestItem[]; }>;
}

function collectRequested(
	request: vscode.TestRunRequest,
	controller: vscode.TestController,
	testItemById: Map<string, vscode.TestItem>,
	specItems: Map<string, vscode.TestItem>
): RequestedTests {
	const exclude = new Set<string>();
	for (const ex of request.exclude ?? []) exclude.add(ex.id);

	const items: vscode.TestItem[] = [];
	const itemById = new Map<string, vscode.TestItem>();
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
			const tests: vscode.TestItem[] = [];
			seed.children.forEach(child => {
				if (!exclude.has(child.id)) tests.push(child);
			});
			if (tests.length === 0) continue;
			items.push(...tests);
			for (const t of tests) itemById.set(t.id, t);
			bySpec.set(specName, { all: tests.length === seed.children.size, tests });
		} else if (testItemById.has(seed.id)) {
			const slash = seed.id.indexOf('/');
			if (slash < 0) continue;
			const specName = seed.id.slice(0, slash);
			const entry = bySpec.get(specName) ?? { all: false, tests: [] };
			entry.tests.push(seed);
			bySpec.set(specName, entry);
			items.push(seed);
			itemById.set(seed.id, seed);
		}
	}

	return { items, itemById, bySpec };
}

function buildFilters(
	requested: RequestedTests,
	specByName: Map<string, SpecFile>
): string[] {
	const coversEverySpec = specByName.size > 0
		&& specByName.size === requested.bySpec.size
		&& [...specByName.keys()].every(specName => requested.bySpec.get(specName)?.all === true);
	if (coversEverySpec) {
		return [];
	}

	const out: string[] = [];
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
	detail: string[];
}

interface ActiveRun {
	requested: RequestedTests;
	run: vscode.TestRun;
	// A failure is reported when the process closes, because its reason may continue on later lines.
	pendingFailures: Map<string, PendingFailure>;
	failureDetailFor?: string;
	reported: Set<string>;
	// Without the summary the runner stopped early, so a test it printed no verdict for was not excluded:
	// it is unaccounted for.
	sawSummary: boolean;
	stderr: string[];
}

async function runWithFilters(
	binding: ProfileBinding,
	filters: string[],
	requested: RequestedTests,
	run: vscode.TestRun,
	token: vscode.CancellationToken
): Promise<void> {
	const args = ['spec-test', ...filters.map(filter => `--filter=${filter}`)];
	log(`Spawning ${binding.binary} ${args.join(' ')}`);
	run.appendOutput(`> ${binding.binary} ${args.join(' ')}\r\n`);

	let child: ChildProcessWithoutNullStreams;
	try {
		child = spawn(binding.binary, args, { cwd: binding.cwd });
	} catch (err) {
		log(`Failed to spawn: ${err}`);
		failAllRequested(run, requested, `Failed to spawn compiler: ${err}`);
		return;
	}

	const cancelSub = token.onCancellationRequested(() => {
		try { child.kill(); } catch { /* ignore */ }
	});

	const active: ActiveRun = {
		requested,
		run,
		pendingFailures: new Map(),
		reported: new Set(),
		sawSummary: false,
		stderr: []
	};

	// Verdicts are on stdout only; stderr is shown, never parsed, so a note there cannot land inside a
	// failure's reason.
	pipeLines(child.stdout, makeVerdictLineHandler(active));
	pipeLines(child.stderr, line => {
		appendRunOutput(run, line);
		active.stderr.push(line.trimEnd());
	});

	await new Promise<void>(resolve => {
		child.on('close', code => {
			cancelSub.dispose();
			flushPendingDetails(active);
			if (code !== 0 && code !== null && !token.isCancellationRequested) {
				run.appendOutput(`\r\nProcess exited with code ${code}\r\n`);
			}
			if (!token.isCancellationRequested) settleUnreported(active, code);
			resolve();
		});
		child.on('error', err => {
			cancelSub.dispose();
			log(`Process error: ${err}`);
			failAllRequested(run, requested, `Process error: ${err}`);
			resolve();
		});
	});
}

function makeVerdictLineHandler(active: ActiveRun) {
	const { requested, run } = active;
	const verdictFor = (id: string): vscode.TestItem | undefined => {
		active.failureDetailFor = undefined;
		const item = requested.itemById.get(id);
		if (item) active.reported.add(id);
		return item;
	};
	return (line: string) => {
		appendRunOutput(run, line);
		const text = line.trimEnd();

		const pass = PASS_LINE_RE.exec(text);
		if (pass) {
			const item = verdictFor(pass[1]);
			if (item) run.passed(item);
			return;
		}

		const fail = FAIL_LINE_RE.exec(text);
		if (fail) {
			const item = verdictFor(fail[1]);
			if (item) {
				active.pendingFailures.set(fail[1], { item, detail: fail[2] ? [fail[2]] : [] });
				active.failureDetailFor = fail[1];
			}
			return;
		}

		const notRun = NOT_RUN_LINE_RE.exec(text);
		if (notRun) {
			const item = verdictFor(notRun[1]);
			if (item) run.skipped(item);
			return;
		}

		if (SUMMARY_RE.test(text)) {
			active.failureDetailFor = undefined;
			active.sawSummary = true;
			return;
		}

		if (active.failureDetailFor && text.trim().length > 0) {
			active.pendingFailures.get(active.failureDetailFor)?.detail.push(text);
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
		active.run.failed(pf.item, message);
	}
	active.pendingFailures.clear();
}

// A requested test with no verdict in a run that reached its summary was passed over on this host, so it is
// skipped. A run the runner refused — a filter that selected nothing here among them — never reaches its
// summary, and each unreported test is errored with the runner's stderr. Which tests this host passes over
// is left to the runner: a copy of its markers here would drift from it.
function settleUnreported(active: ActiveRun, code: number | null): void {
	const said = active.stderr.filter(line => line.length > 0).join('\n');
	const unreported = `spec-test exited with code ${code} before reporting this test`;
	const reason = said.length > 0 ? `${unreported}:\n${said}` : `${unreported}; its output is in the run log.`;
	for (const item of active.requested.items) {
		if (active.reported.has(item.id)) continue;

		if (active.sawSummary) {
			active.run.skipped(item);
		} else {
			active.run.errored(item, new vscode.TestMessage(reason));
		}
	}
}

function failAllRequested(run: vscode.TestRun, requested: RequestedTests, message: string): void {
	for (const item of requested.items) run.errored(item, new vscode.TestMessage(message));
}
