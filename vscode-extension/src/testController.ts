import { spawn } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { debugToExit, NoCompilerMessage } from './debugAdapter';
import { log } from './logger';
import { registerAll } from './registration';
import { isMaxonCheckout, registerSpecTestController } from './specTestController';
import { appendRunOutput, childList, pipeLines } from './testItems';
import {
	debuggedTestVerdict,
	isIgnoredDirectory,
	isTestFileName,
	listedTestFor,
	parseTestDeclarations,
	parseTestListDocument,
	parseTestRunDocument,
	pathKey,
	removeStagedTestBinary,
	resultKey,
	StagedTestBinary,
	stageTestBinary,
	TestFileGlob,
	testFilterFor,
	TestListDocument,
	TestListExitCode,
	testKey,
	TestProjectExitCode,
	testProjectDirectory,
	TestRunDocument,
	TestVerdict,
	verdictFor
} from './unitTestModel';

const IGNORE_MARKER_GLOB = '**/.maxonignore';
const COMMAND_ECHO_PREFIX = '> ';
const JSON_FLAG = '--json';
const FILTER_FLAG = '--filter=';
const LIST_FLAG = '--list';
const BUILD_FLAG = '--build';
const SELECT_FLAG = '--select=';
const NOT_LISTED_MESSAGE = 'maxon test did not list this test. It compiles the files as saved on disk, so a test that is unsaved or was renamed is not among them.';

interface DeclaredTestItem {
	file: string;
	name: string;
}

export interface UnitTestController {
	controller: vscode.TestController;
	runProfile: vscode.TestRunProfile;
	debugProfile: vscode.TestRunProfile;
}

export interface RegisteredTestControllers {
	registration: vscode.Disposable;
	unitTests: UnitTestController;
}

/**
 * The Test Explorer: every `test` declaration in the workspace's `.maxtest` files, run by the compiler the
 * language server uses. The checkout's spec suite gets a controller of its own, only in the checkout.
 *
 * `compilerExecutable` is asked at run time, because the compiler is found (or installed) after activation
 * registers this.
 */
export function registerTestControllers(compilerExecutable: () => string | undefined): RegisteredTestControllers {
	const unitTests = registerUnitTestController(compilerExecutable);
	const checkouts = (vscode.workspace.workspaceFolders ?? []).map(folder => folder.uri.fsPath).filter(isMaxonCheckout);

	const registration = registerAll([
		() => unitTests.registration,
		...checkouts.map(checkout => () => {
			log(`${checkout} is the Maxon checkout; registering the spec suite`);
			return registerSpecTestController(checkout);
		})
	]);

	return { registration, unitTests };
}

function registerUnitTestController(compilerExecutable: () => string | undefined): UnitTestController & { registration: vscode.Disposable; } {
	const controller = vscode.tests.createTestController('maxonTests', 'Maxon Tests');
	const fileItems = new Map<string, vscode.TestItem>();
	const declared = new Map<string, DeclaredTestItem>();

	function syncFile(uri: vscode.Uri): void {
		const file = uri.fsPath;
		const folder = vscode.workspace.getWorkspaceFolder(uri);

		// `maxon test` never compiles a file beneath a `.maxonignore`, so it has no tests to show.
		if (!folder || !isTestFileName(file) || isIgnoredDirectory(path.dirname(file))) {
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
		const uris = await vscode.workspace.findFiles(TestFileGlob);
		const found = new Set(uris.map(uri => pathKey(uri.fsPath)));

		for (const [key, item] of [...fileItems]) {
			if (!found.has(key) && item.uri) removeFile(item.uri);
		}
		for (const uri of uris) syncFile(uri);

		log(`Discovered ${declared.size} test(s) across ${fileItems.size} test file(s)`);
	}

	controller.refreshHandler = () => discoverAll();
	discoverAll().catch(err => log(`Test discovery failed: ${err}`));

	const testFileWatcher = vscode.workspace.createFileSystemWatcher(TestFileGlob);
	testFileWatcher.onDidCreate(syncFile);
	testFileWatcher.onDidChange(syncFile);
	testFileWatcher.onDidDelete(removeFile);

	// A marker added or removed changes which test files any compile can see.
	const ignoreMarkerWatcher = vscode.workspace.createFileSystemWatcher(IGNORE_MARKER_GLOB, false, true, false);
	const rediscover = () => { discoverAll().catch(err => log(`Test discovery failed: ${err}`)); };
	ignoreMarkerWatcher.onDidCreate(rediscover);
	ignoreMarkerWatcher.onDidDelete(rediscover);

	const projectCommands = new Map<string, Promise<void>>();
	const runProfile = createProjectRunProfile('Run', vscode.TestRunProfileKind.Run, runProject, true);
	const debugProfile = createProjectRunProfile('Debug', vscode.TestRunProfileKind.Debug, debugProject, false);

	function createProjectRunProfile(label: string, kind: vscode.TestRunProfileKind, perform: ProjectRunner, isDefault: boolean): vscode.TestRunProfile {
		return controller.createRunProfile(label, kind, (request, token) => startTestRun(request, token, perform), isDefault);
	}

	// Every `maxon test` in one project stages into and builds over the same `.maxon/test/` tree, so
	// two commands in one project run one after the other.
	function exclusivelyInProject<T>(projectDirectory: string, work: () => Promise<T>): Promise<T> {
		const key = pathKey(projectDirectory);
		const previous = projectCommands.get(key) ?? Promise.resolve();
		const result = previous.then(work);
		const settled = result.then(() => undefined, () => undefined);

		projectCommands.set(key, settled);
		void settled.then(() => {
			if (projectCommands.get(key) === settled) projectCommands.delete(key);
		});

		return result;
	}

	async function startTestRun(request: vscode.TestRunRequest, token: vscode.CancellationToken, perform: ProjectRunner): Promise<void> {
		const run = controller.createTestRun(request);

		try {
			const requested = requestedTests(request);
			if (requested.length === 0) return;

			const compiler = compilerExecutable();
			if (!compiler) {
				erroredAll(run, requested, NoCompilerMessage);
				return;
			}

			for (const item of requested) run.enqueued(item);

			for (const project of groupByProject(requested)) {
				if (token.isCancellationRequested) break;
				await perform(compiler, project, run, token);
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

	interface TestProject {
		folder: vscode.WorkspaceFolder;
		projectDirectory: string;
		workingDirectory: string;
	}

	interface ProjectRun extends TestProject {
		tests: vscode.TestItem[];
		filter: string | undefined;
	}

	type ProjectRunner = (compiler: string, project: ProjectRun, run: vscode.TestRun, token: vscode.CancellationToken) => Promise<void>;

	function projectOf(file: string): TestProject {
		const folder = vscode.workspace.getWorkspaceFolder(vscode.Uri.file(file));
		if (!folder) throw new Error(`${file} is in no workspace folder`);

		return {
			folder,
			projectDirectory: testProjectDirectory(file, folder.uri.fsPath),
			workingDirectory: folder.uri.fsPath
		};
	}

	function groupByProject(requested: vscode.TestItem[]): ProjectRun[] {
		const located = new Map<string, TestProject>();
		const locate = (file: string) => {
			const key = pathKey(file);
			let project = located.get(key);
			if (!project) {
				project = projectOf(file);
				located.set(key, project);
			}
			return project;
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
				folder: group.folder,
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

	function testCommandArguments(project: ProjectRun, modeFlags: string[]): string[] {
		const args = ['test', project.projectDirectory, ...modeFlags, JSON_FLAG];
		if (project.filter !== undefined) args.push(`${FILTER_FLAG}${project.filter}`);
		return args;
	}

	async function runTestCommand(compiler: string, args: string[], project: ProjectRun, run: vscode.TestRun, token: vscode.CancellationToken): Promise<CompilerExit | undefined> {
		log(`Running ${compiler} ${args.join(' ')} in ${project.workingDirectory}`);
		run.appendOutput(`${COMMAND_ECHO_PREFIX}${compiler} ${args.join(' ')}\r\n`);

		const outcome = await runCompiler(compiler, args, project.workingDirectory, run, token);
		switch (outcome.kind) {
			case 'exited':
				return outcome;
			case 'cancelled':
				return undefined;
			case 'failedToStart':
				erroredAll(run, project.tests, `Could not run ${compiler}: ${outcome.error}`);
				return undefined;
			default:
				throw new Error(`runTestCommand: unhandled compiler outcome ${JSON.stringify(outcome)}`);
		}
	}

	async function runProject(compiler: string, project: ProjectRun, run: vscode.TestRun, token: vscode.CancellationToken): Promise<void> {
		for (const item of project.tests) run.started(item);

		const outcome = await exclusivelyInProject(project.projectDirectory, () => runTestCommand(compiler, testCommandArguments(project, []), project, run, token));
		if (!outcome) return;

		if (outcome.code !== TestProjectExitCode.AllPassed && outcome.code !== TestProjectExitCode.TestsFailed) {
			erroredAll(run, project.tests, `maxon test could not run the tests (exit code ${outcome.code})${compilerSaid(outcome)}`);
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

	interface DebuggableBuild {
		document: TestListDocument;
		binary: StagedTestBinary;
	}

	async function debugProject(compiler: string, project: ProjectRun, run: vscode.TestRun, token: vscode.CancellationToken): Promise<void> {
		const build = await exclusivelyInProject(project.projectDirectory, () => buildForDebugging(compiler, project, run, token));
		if (!build) return;

		try {
			for (const item of project.tests) {
				if (token.isCancellationRequested) return;
				await debugListedTest(item, build, project, run, token);
			}
		} finally {
			await removeStagedTestBinary(build.binary).catch(err => log(`Could not remove the debugged copy of the test binary in ${build.binary.directory}: ${err}`));
		}
	}

	async function buildForDebugging(compiler: string, project: ProjectRun, run: vscode.TestRun, token: vscode.CancellationToken): Promise<DebuggableBuild | undefined> {
		const outcome = await runTestCommand(compiler, testCommandArguments(project, [LIST_FLAG, BUILD_FLAG]), project, run, token);
		if (!outcome) return undefined;

		if (outcome.code === TestListExitCode.NoneListed) {
			erroredAll(run, project.tests, NOT_LISTED_MESSAGE);
			return undefined;
		}

		if (outcome.code !== TestListExitCode.Listed) {
			erroredAll(run, project.tests, `maxon test could not build the tests for debugging (exit code ${outcome.code})${compilerSaid(outcome)}`);
			return undefined;
		}

		let document: TestListDocument;
		try {
			document = parseTestListDocument(outcome.stdout);
		} catch (err) {
			erroredAll(run, project.tests, `maxon test built the tests without a listing this extension could read: ${err}\n${outcome.stdout.trim()}`);
			return undefined;
		}

		try {
			return { document, binary: stageTestBinary(document.binary) };
		} catch (err) {
			erroredAll(run, project.tests, `Could not copy the test binary ${document.binary} aside for debugging: ${err}`);
			return undefined;
		}
	}

	async function debugListedTest(item: vscode.TestItem, build: DebuggableBuild, project: ProjectRun, run: vscode.TestRun, token: vscode.CancellationToken): Promise<void> {
		const test = declaredTest(item);
		const listed = listedTestFor(build.document, test.file, test.name, project.workingDirectory);
		if (!listed) {
			run.errored(item, new vscode.TestMessage(NOT_LISTED_MESSAGE));
			return;
		}

		run.started(item);

		const ended = await debugToExit(project.folder, {
			name: `Debug test: ${test.name}`,
			program: build.binary.program,
			args: [`${SELECT_FLAG}${listed.select}`],
			cwd: project.workingDirectory
		}, { testRun: run }, token);

		switch (ended.kind) {
			case 'cancelled':
				return;
			case 'ended':
				run.errored(item, new vscode.TestMessage(ended.reason));
				return;
			case 'exited':
				applyVerdict(run, item, debuggedTestVerdict(ended.code));
				return;
			default:
				throw new Error(`debugListedTest: unhandled debug run outcome ${JSON.stringify(ended)}`);
		}
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
	return { controller, runProfile, debugProfile, registration: new vscode.Disposable(() => disposables.forEach(d => d.dispose())) };
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
	| { kind: 'failedToStart'; error: string; }
	| { kind: 'cancelled'; };

type CompilerExit = Extract<CompilerOutcome, { kind: 'exited'; }>;

function compilerSaid(outcome: { stdout: string; stderr: string; }): string {
	const said = outcome.stderr.trim() || outcome.stdout.trim();
	return said ? `:\n${said}` : '.';
}

function runCompiler(
	compiler: string,
	args: string[],
	cwd: string,
	run: vscode.TestRun,
	token: vscode.CancellationToken
): Promise<CompilerOutcome> {
	if (token.isCancellationRequested) return Promise.resolve({ kind: 'cancelled' });

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
		child.on('close', code => settle(token.isCancellationRequested ? { kind: 'cancelled' } : { kind: 'exited', code, stdout, stderr }));
	});
}
