import { spawn } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { log } from './logger';
import { isMaxonCheckout, registerSpecTestController } from './specTestController';
import { appendRunOutput, childList, pipeLines } from './testItems';
import {
	isIgnoredDirectory,
	parseTestDeclarations,
	parseTestRunDocument,
	pathKey,
	resultKey,
	TestFileSuffix,
	testFilterFor,
	testKey,
	TestProjectExitCode,
	testProjectDirectory,
	TestRunDocument,
	TestVerdict,
	verdictFor
} from './unitTestModel';

// The compiler folds case when it recognises a test file, so the glob does too.
const TEST_FILE_GLOB = '**/*.[tT][eE][sS][tT].maxon';
const IGNORE_MARKER_GLOB = '**/.maxonignore';

interface DeclaredTestItem {
	file: string;
	name: string;
}

/**
 * The Test Explorer: every `test` declaration in the workspace's `*.test.maxon` files, run by the compiler the
 * language server uses. The checkout's spec suite gets a controller of its own, only in the checkout.
 *
 * `compilerExecutable` is asked at run time, because the compiler is found (or installed) after activation
 * registers this.
 */
export function registerTestControllers(compilerExecutable: () => string | undefined): vscode.Disposable {
	const disposables: vscode.Disposable[] = [registerUnitTestController(compilerExecutable)];

	for (const folder of vscode.workspace.workspaceFolders ?? []) {
		if (isMaxonCheckout(folder.uri.fsPath)) {
			log(`${folder.uri.fsPath} is the Maxon checkout; registering the spec suite`);
			disposables.push(registerSpecTestController(folder.uri.fsPath));
		}
	}

	return vscode.Disposable.from(...disposables);
}

function registerUnitTestController(compilerExecutable: () => string | undefined): vscode.Disposable {
	const controller = vscode.tests.createTestController('maxonTests', 'Maxon Tests');
	const fileItems = new Map<string, vscode.TestItem>();
	const declared = new Map<string, DeclaredTestItem>();

	function syncFile(uri: vscode.Uri): void {
		const file = uri.fsPath;
		const folder = vscode.workspace.getWorkspaceFolder(uri);

		// `maxon test` never compiles a file beneath a `.maxonignore`, so it has no tests to show.
		if (!folder || !path.basename(file).toLowerCase().endsWith(TestFileSuffix) || isIgnoredDirectory(path.dirname(file))) {
			removeFile(uri);
			return;
		}

		let content: string;
		try {
			content = fs.readFileSync(file, 'utf8');
		} catch (err) {
			log(`Could not read ${file}: ${err}`);
			removeFile(uri);
			return;
		}

		let fileItem = fileItems.get(pathKey(file));
		if (!fileItem) {
			fileItem = controller.createTestItem(uri.toString(), vscode.workspace.asRelativePath(uri, true), uri);
			controller.items.add(fileItem);
			fileItems.set(pathKey(file), fileItem);
		}

		const children: vscode.TestItem[] = [];
		for (const test of parseTestDeclarations(content)) {
			const id = testKey(file, test.name);
			const item = fileItem.children.get(id) ?? controller.createTestItem(id, test.name, uri);
			const position = new vscode.Position(test.line, test.column);
			item.range = new vscode.Range(position, position);
			declared.set(id, { file, name: test.name });
			children.push(item);
		}

		const kept = new Set(children.map(child => child.id));
		for (const stale of childList(fileItem)) {
			if (!kept.has(stale.id)) declared.delete(stale.id);
		}
		fileItem.children.replace(children);
	}

	function removeFile(uri: vscode.Uri): void {
		const key = pathKey(uri.fsPath);
		const fileItem = fileItems.get(key);
		if (!fileItem) return;

		for (const child of childList(fileItem)) declared.delete(child.id);
		controller.items.delete(fileItem.id);
		fileItems.delete(key);
	}

	async function discoverAll(): Promise<void> {
		const uris = await vscode.workspace.findFiles(TEST_FILE_GLOB);
		const found = new Set(uris.map(uri => pathKey(uri.fsPath)));

		for (const [key, item] of [...fileItems]) {
			if (!found.has(key) && item.uri) removeFile(item.uri);
		}
		for (const uri of uris) syncFile(uri);

		log(`Discovered ${declared.size} test(s) across ${fileItems.size} test file(s)`);
	}

	controller.refreshHandler = () => discoverAll();
	discoverAll().catch(err => log(`Test discovery failed: ${err}`));

	const testFileWatcher = vscode.workspace.createFileSystemWatcher(TEST_FILE_GLOB);
	testFileWatcher.onDidCreate(syncFile);
	testFileWatcher.onDidChange(syncFile);
	testFileWatcher.onDidDelete(removeFile);

	// A marker added or removed changes which test files any compile can see.
	const ignoreMarkerWatcher = vscode.workspace.createFileSystemWatcher(IGNORE_MARKER_GLOB, false, true, false);
	const rediscover = () => { discoverAll().catch(err => log(`Test discovery failed: ${err}`)); };
	ignoreMarkerWatcher.onDidCreate(rediscover);
	ignoreMarkerWatcher.onDidDelete(rediscover);

	controller.createRunProfile(
		'Run',
		vscode.TestRunProfileKind.Run,
		(request, token) => runTests(request, token),
		true
	);

	async function runTests(request: vscode.TestRunRequest, token: vscode.CancellationToken): Promise<void> {
		const run = controller.createTestRun(request);

		try {
			const requested = requestedTests(request);
			if (requested.length === 0) return;

			const compiler = compilerExecutable();
			if (!compiler) {
				const message = new vscode.TestMessage('No Maxon compiler was found. Set `maxon.serverPath` or install Maxon, then reload the window.');
				for (const item of requested) run.errored(item, message);
				return;
			}

			for (const item of requested) run.enqueued(item);

			for (const project of groupByProject(requested)) {
				if (token.isCancellationRequested) break;
				await runProject(compiler, project, run, token);
			}
		} catch (err) {
			log(`Test run failed: ${err}`);
			run.appendOutput(`Test run failed: ${err}\r\n`);
		} finally {
			run.end();
		}
	}

	function requestedTests(request: vscode.TestRunRequest): vscode.TestItem[] {
		const excluded = new Set((request.exclude ?? []).map(item => item.id));
		const seeds: vscode.TestItem[] = [];
		if (request.include) {
			seeds.push(...request.include);
		} else {
			controller.items.forEach(item => seeds.push(item));
		}

		const out = new Map<string, vscode.TestItem>();
		for (const seed of seeds) {
			if (excluded.has(seed.id)) continue;

			if (declared.has(seed.id)) {
				out.set(seed.id, seed);
				continue;
			}

			for (const child of childList(seed)) {
				if (!excluded.has(child.id)) out.set(child.id, child);
			}
		}
		return [...out.values()];
	}

	interface ProjectRun {
		projectDirectory: string;
		workingDirectory: string;
		tests: vscode.TestItem[];
		filter: string | undefined;
	}

	function groupByProject(requested: vscode.TestItem[]): ProjectRun[] {
		const projectOf = new Map<string, { projectDirectory: string; workingDirectory: string; }>();
		const locate = (file: string) => {
			const key = pathKey(file);
			let located = projectOf.get(key);
			if (!located) {
				const folder = vscode.workspace.getWorkspaceFolder(vscode.Uri.file(file));
				if (!folder) throw new Error(`${file} is in no workspace folder`);
				located = {
					projectDirectory: testProjectDirectory(file, folder.uri.fsPath),
					workingDirectory: folder.uri.fsPath
				};
				projectOf.set(key, located);
			}
			return located;
		};

		const requestedIds = new Set(requested.map(item => item.id));
		const groups = new Map<string, ProjectRun & { wholeProject: boolean; }>();

		for (const item of requested) {
			const test = declaredTest(item);
			const located = locate(test.file);
			const key = pathKey(located.projectDirectory);
			let group = groups.get(key);
			if (!group) {
				group = { ...located, tests: [], filter: undefined, wholeProject: true };
				groups.set(key, group);
			}
			group.tests.push(item);
		}

		// A project runs unfiltered only when every test discovered in it is requested.
		for (const [id, test] of declared) {
			if (requestedIds.has(id)) continue;
			const group = groups.get(pathKey(locate(test.file).projectDirectory));
			if (group) group.wholeProject = false;
		}

		return [...groups.values()].map(group => {
			const wholeFiles: string[] = [];
			const testNames: string[] = [];

			for (const fileItem of fileItems.values()) {
				const tests = childList(fileItem).filter(child => requestedIds.has(child.id));
				if (tests.length === 0 || !group.tests.includes(tests[0])) continue;

				if (tests.length === fileItem.children.size) {
					wholeFiles.push(declaredTest(tests[0]).file);
				} else {
					testNames.push(...tests.map(child => declaredTest(child).name));
				}
			}

			return {
				projectDirectory: group.projectDirectory,
				workingDirectory: group.workingDirectory,
				tests: group.tests,
				filter: testFilterFor({ wholeFiles, testNames }, group.workingDirectory, group.wholeProject)
			};
		});
	}

	function declaredTest(item: vscode.TestItem): DeclaredTestItem {
		const test = declared.get(item.id);
		if (!test) throw new Error(`The test item '${item.label}' is not a discovered test`);
		return test;
	}

	async function runProject(compiler: string, project: ProjectRun, run: vscode.TestRun, token: vscode.CancellationToken): Promise<void> {
		const args = ['test', project.projectDirectory, '--json'];
		if (project.filter !== undefined) args.push(`--filter=${project.filter}`);

		log(`Running ${compiler} ${args.join(' ')} in ${project.workingDirectory}`);
		run.appendOutput(`> ${compiler} ${args.join(' ')}\r\n`);
		for (const item of project.tests) run.started(item);

		const outcome = await runCompiler(compiler, args, project.workingDirectory, run, token);
		if (token.isCancellationRequested) return;

		if (outcome.kind === 'failedToStart') {
			erroredAll(run, project.tests, `Could not run ${compiler}: ${outcome.error}`);
			return;
		}

		if (outcome.code !== TestProjectExitCode.AllPassed && outcome.code !== TestProjectExitCode.TestsFailed) {
			const said = outcome.stderr.trim() || outcome.stdout.trim();
			erroredAll(run, project.tests, `maxon test could not run the tests (exit code ${outcome.code})${said ? `:\n${said}` : '.'}`);
			return;
		}

		let document: TestRunDocument;
		try {
			document = parseTestRunDocument(outcome.stdout);
		} catch (err) {
			erroredAll(run, project.tests, `maxon test exited with code ${outcome.code} without a report this extension could read: ${err}\n${outcome.stdout.trim()}`);
			return;
		}

		reportResults(document, project, run);
	}

	function reportResults(document: TestRunDocument, project: ProjectRun, run: vscode.TestRun): void {
		const byKey = new Map(project.tests.map(item => {
			const test = declaredTest(item);
			return [testKey(test.file, test.name), item] as const;
		}));
		const reported = new Set<vscode.TestItem>();

		for (const result of document.results) {
			const item = byKey.get(resultKey(result, project.workingDirectory));
			if (!item) continue;

			reported.add(item);
			const test = declaredTest(item);

			if (result.output) {
				run.appendOutput(result.output.replace(/\r?\n/g, '\r\n') + '\r\n', itemLocation(item), item);
			}
			applyVerdict(run, item, verdictFor(result, test.file, project.workingDirectory));
		}

		for (const item of project.tests) {
			if (reported.has(item)) continue;

			if (document.reason) {
				run.errored(item, new vscode.TestMessage(`maxon test ran nothing: ${document.reason}`));
			} else {
				run.errored(item, new vscode.TestMessage('maxon test reported no result for this test. It compiles the files as saved on disk, so a test that is unsaved or was renamed is not among its results.'));
			}
		}
	}

	const disposables = [controller, testFileWatcher, ignoreMarkerWatcher];
	return new vscode.Disposable(() => disposables.forEach(d => d.dispose()));
}

function itemLocation(item: vscode.TestItem): vscode.Location | undefined {
	return item.uri && item.range ? new vscode.Location(item.uri, item.range) : undefined;
}

function applyVerdict(run: vscode.TestRun, item: vscode.TestItem, verdict: TestVerdict): void {
	if (verdict.kind === 'passed') {
		run.passed(item, verdict.durationMs);
		return;
	}

	const message = new vscode.TestMessage(verdict.message);
	message.location = verdict.location
		? new vscode.Location(vscode.Uri.file(verdict.location.file), new vscode.Position(verdict.location.line, 0))
		: itemLocation(item);

	if (verdict.kind === 'failed') {
		run.failed(item, message, verdict.durationMs);
	} else {
		run.errored(item, message, verdict.durationMs);
	}
}

function erroredAll(run: vscode.TestRun, items: vscode.TestItem[], text: string): void {
	const message = new vscode.TestMessage(text);
	for (const item of items) run.errored(item, message);
}

type CompilerOutcome =
	| { kind: 'exited'; code: number | null; stdout: string; stderr: string; }
	| { kind: 'failedToStart'; error: string; };

function runCompiler(
	compiler: string,
	args: string[],
	cwd: string,
	run: vscode.TestRun,
	token: vscode.CancellationToken
): Promise<CompilerOutcome> {
	return new Promise(resolve => {
		const child = spawn(compiler, args, { cwd });
		let stdout = '';
		let stderr = '';
		let settled = false;
		const settle = (outcome: CompilerOutcome) => {
			if (settled) return;
			settled = true;
			cancellation.dispose();
			resolve(outcome);
		};

		const cancellation = token.onCancellationRequested(() => child.kill());

		child.stdout.setEncoding('utf8');
		child.stdout.on('data', (chunk: string) => { stdout += chunk; });
		pipeLines(child.stderr, line => {
			stderr += line + '\n';
			appendRunOutput(run, line);
		});

		child.on('error', err => settle({ kind: 'failedToStart', error: err.message }));
		child.on('close', code => settle({ kind: 'exited', code, stdout, stderr }));
	});
}
