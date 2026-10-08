import * as cp from 'child_process';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import * as vscode from 'vscode';
import {
	LanguageClient,
	LanguageClientOptions,
	RevealOutputChannelOn,
	ServerOptions,
	State
} from 'vscode-languageclient/node';
import {
	CompilerSearch,
	CompilerSource,
	compilerBinaryName,
	compilerToRestartOn,
	isExecutableFile,
	isMaxonCheckout,
	locateCompiler,
	LookupNotice,
	watchedCompilers
} from './compilerLocator';
import { log, initLogger } from './logger';
import { RestartCoordinator, systemTimers } from './restartCoordinator';
import { CompilerExplorerViewProvider } from './compilerExplorerPanel';
import { registerTestControllers, UnitTestController } from './testController';
import { registerDebugging } from './debugAdapter';

interface ExtensionState {
	client: LanguageClient;
	compilerExecutable: string;
	clientOptions: LanguageClientOptions;
	context: vscode.ExtensionContext;
}

let state: ExtensionState | undefined;
let statusBarItem: vscode.StatusBarItem | undefined;
let clientSubscriptions: vscode.Disposable[] = [];
let compilerWatchers: vscode.Disposable[] = [];
let unitTests: UnitTestController | undefined;

const ProjectLoadingNotification = 'maxon/projectLoading';
const RestartLanguageServerCommand = 'maxon.restartLanguageServer';
const ShowLanguageServerOutputCommand = 'maxon.showLanguageServerOutput';

interface ProjectLoadingParams {
	rootPath: string;
	loading: boolean;
}

const loadingProjectRoots = new Set<string>();

const isWindows = os.platform() === 'win32';

export function getClient(): LanguageClient | undefined {
	return state?.client;
}

interface ProjectInfo {
	rootPath: string;
	isSingleFile: boolean;
	fileCount: number;
}

interface ListProjectsResponse {
	projects: ProjectInfo[];
}

let lastClientState: State = State.Stopped;
type ProjectsView =
	| { kind: 'loading'; }
	| { kind: 'loaded'; projects: ProjectInfo[]; }
	| { kind: 'unavailable'; reason: string; };

let projectsView: ProjectsView = { kind: 'loading' };
let projectRefreshTimer: NodeJS.Timeout | undefined;

function buildTooltip(): vscode.MarkdownString {
	const md = new vscode.MarkdownString(undefined, true);
	// Root paths are interpolated into this Markdown, so trust covers these two command links alone.
	md.isTrusted = { enabledCommands: [RestartLanguageServerCommand, ShowLanguageServerOutputCommand] };
	md.supportThemeIcons = true;

	const stateLabel =
		lastClientState === State.Running ? '$(check) Running' :
		lastClientState === State.Starting ? '$(sync~spin) Starting…' :
		'$(error) Stopped';
	md.appendMarkdown(`**Maxon Language Server** — ${stateLabel}\n\n`);

	appendCompiler(md);

	if (lastClientState === State.Running) {
		appendProjects(md);
	}

	md.appendMarkdown('\n\n---\n\n');
	md.appendMarkdown(`[$(debug-restart) Restart](command:${RestartLanguageServerCommand} "Restart the Maxon Language Server")`);
	md.appendMarkdown(' · ');
	md.appendMarkdown(`[$(output) Show Output](command:${ShowLanguageServerOutputCommand} "Open the Maxon Language Server output")`);
	return md;
}

function compilerVersionText(client: LanguageClient, clientState: State): string {
	const reported = client.initializeResult?.serverInfo?.version;
	if (reported) {
		return reported;
	}

	switch (clientState) {
		case State.Running:
			return 'unknown (this compiler does not report its version)';
		case State.Starting:
			return 'reported once the language server has started';
		case State.Stopped:
			return 'unknown (the language server is not running)';
		default:
			throw new Error(`compilerVersionText: unhandled client state ${clientState}`);
	}
}

function appendCompiler(md: vscode.MarkdownString) {
	if (!state) return;

	md.appendMarkdown('**Compiler**\n\n');
	md.appendMarkdown('Version: ');
	md.appendText(compilerVersionText(state.client, lastClientState));
	md.appendMarkdown('\n\nLocation: ');
	md.appendText(state.compilerExecutable);
	md.appendMarkdown('\n\n');
}

function appendProjects(md: vscode.MarkdownString) {
	if (loadingProjectRoots.size > 0) {
		md.appendMarkdown('**Loading projects**\n\n');
		for (const root of loadingProjectRoots) {
			md.appendMarkdown(`- $(sync~spin) \`${root}\`\n`);
		}
		md.appendMarkdown('\n');
	}

	switch (projectsView.kind) {
		case 'loading':
			md.appendMarkdown('_Loading projects…_');
			return;
		case 'unavailable':
			md.appendMarkdown('_Could not list the loaded projects:_ ');
			md.appendText(projectsView.reason);
			return;
		case 'loaded':
			break;
		default:
			throw new Error(`appendProjects: unhandled projects view ${JSON.stringify(projectsView)}`);
	}

	if (projectsView.projects.length === 0) {
		md.appendMarkdown('_No projects loaded_');
		return;
	}

	md.appendMarkdown('**Loaded projects**\n\n');
	for (const p of projectsView.projects) {
		md.appendMarkdown(projectLine(p));
	}
}

function projectLine(p: ProjectInfo): string {
	if (p.isSingleFile) {
		return `- \`${p.rootPath}\`\n`;
	}

	const fileText = p.fileCount === 1 ? '1 file' : `${p.fileCount} files`;
	return `- \`${p.rootPath}\` _(project, ${fileText})_\n`;
}

function updateStatusBar() {
	if (!statusBarItem) return;
	switch (lastClientState) {
		case State.Running:
			statusBarItem.text = '$(maxon-logo)';
			statusBarItem.backgroundColor = loadingProjectRoots.size > 0
				? new vscode.ThemeColor('statusBarItem.warningBackground')
				: undefined;
			break;
		case State.Starting:
			statusBarItem.text = '$(sync~spin)';
			statusBarItem.backgroundColor = undefined;
			break;
		case State.Stopped:
			statusBarItem.text = '$(maxon-logo)';
			statusBarItem.backgroundColor = new vscode.ThemeColor('statusBarItem.errorBackground');
			break;
		default:
			throw new Error(`updateStatusBar: unhandled client state ${lastClientState}`);
	}
	statusBarItem.tooltip = buildTooltip();
}

async function refreshProjects() {
	const client = state?.client;
	if (!client || client.state !== State.Running) {
		projectsView = { kind: 'loading' };
		updateStatusBar();
		return;
	}
	try {
		const response = await client.sendRequest<ListProjectsResponse>('maxon/listProjects', {});
		if (!Array.isArray(response?.projects)) {
			throw new Error(`the answer has no projects list: ${JSON.stringify(response)}`);
		}
		projectsView = { kind: 'loaded', projects: response.projects };
	} catch (error) {
		log(`maxon/listProjects failed: ${error}`);
		projectsView = { kind: 'unavailable', reason: error instanceof Error ? error.message : String(error) };
	}
	updateStatusBar();
}

function scheduleProjectRefresh() {
	if (projectRefreshTimer) clearTimeout(projectRefreshTimer);
	projectRefreshTimer = setTimeout(() => refreshProjects(), 500);
}

function disposeClientSubscriptions() {
	for (const subscription of clientSubscriptions) {
		subscription.dispose();
	}
	clientSubscriptions = [];
}

function subscribeToClient(client: LanguageClient) {
	disposeClientSubscriptions();
	loadingProjectRoots.clear();
	applyClientState(client.state);
	clientSubscriptions.push(
		client.onDidChangeState((e) => applyClientState(e.newState)),
		client.onNotification(ProjectLoadingNotification, onProjectLoading)
	);
}

function applyClientState(newState: State) {
	lastClientState = newState;
	if (newState !== State.Running) {
		projectsView = { kind: 'loading' };
		loadingProjectRoots.clear();
	}
	updateStatusBar();
	if (newState === State.Running) {
		scheduleProjectRefresh();
	}
}

function onProjectLoading(params: ProjectLoadingParams) {
	if (typeof params?.rootPath !== 'string' || typeof params?.loading !== 'boolean') {
		throw new Error(`${ProjectLoadingNotification}: malformed params ${JSON.stringify(params)}`);
	}

	if (params.loading) {
		loadingProjectRoots.add(params.rootPath);
		updateStatusBar();
		return;
	}

	loadingProjectRoots.delete(params.rootPath);
	updateStatusBar();
	scheduleProjectRefresh();
}

/**
 * Remove the server copy (`maxon-lsp`) and its `stdlib` link from global storage, where an install
 * upgraded from an extension that ran the server from a copy still has them. Nothing reads either:
 * the server is the compiler itself, which a rebuild or an install renames aside while it runs.
 *
 * ⛔ The link is removed only if it IS a link — unlinking it leaves the standard library it points at
 * untouched, and anything else in that place is left alone and reported.
 */
async function removeServerCopy(storageDir: string): Promise<void> {
	const copy = path.join(storageDir, isWindows ? 'maxon-lsp.exe' : 'maxon-lsp');
	const link = path.join(storageDir, 'stdlib');

	try {
		await fs.promises.unlink(copy);
		log(`Removed ${copy}`);
	} catch (error) {
		if ((error as NodeJS.ErrnoException).code !== 'ENOENT') {
			log(`Could not remove ${copy}: ${error}`);
		}
	}

	let existing: fs.Stats;
	try {
		existing = await fs.promises.lstat(link);
	} catch {
		return;
	}

	if (!existing.isSymbolicLink()) {
		log(`${link} exists and is not a link; leaving it`);
		return;
	}

	try {
		await fs.promises.unlink(link);
		log(`Removed ${link}`);
	} catch (error) {
		log(`Could not remove ${link}: ${error}`);
	}
}

function serverOptionsFor(compilerExecutable: string): ServerOptions {
	return {
		command: compilerExecutable,
		args: ['lsp-server']
	};
}

// `revealOutputChannelOn: Never` quiets only the client's ordinary error notifications. A start
// failure, a crash with no restart left and a failed restart pass `'force'`, which ignores that
// setting.
class QuietLanguageClient extends LanguageClient {
	public override error(message: string, data?: any, _showNotification?: boolean | 'force'): void {
		super.error(message, data, false);
	}
}

function createClient(compilerExecutable: string, clientOptions: LanguageClientOptions): LanguageClient {
	return new QuietLanguageClient(
		'maxonLanguageServer',
		'Maxon Language Server',
		serverOptionsFor(compilerExecutable),
		clientOptions
	);
}

function workspaceFolderPaths(): string[] {
	return (vscode.workspace.workspaceFolders ?? []).map(folder => folder.uri.fsPath);
}

function searchFor(ctx: vscode.ExtensionContext): CompilerSearch {
	return {
		configuredPath: vscode.workspace.getConfiguration('maxon').get<string>('serverPath')?.trim() ?? '',
		workspaceFolders: workspaceFolderPaths(),
		extensionCheckout: path.join(ctx.extensionPath, '..'),
		installRoot: installRoot(),
		binaryName: compilerBinaryName,
		findOnPath,
		isExecutable: isExecutableFile,
		isCheckout: isMaxonCheckout
	};
}

async function findCompiler(ctx: vscode.ExtensionContext): Promise<string> {
	const lookup = await locateCompiler(searchFor(ctx));

	for (const notice of lookup.notices) {
		reportLookupNotice(notice);
	}

	if (!lookup.choice) {
		return '';
	}

	log(`Using ${CompilerSourceDescriptions[lookup.choice.source]}: ${lookup.choice.executable}`);
	return lookup.choice.executable;
}

const CompilerSourceDescriptions: Record<CompilerSource, string> = {
	setting: 'maxon.serverPath',
	checkout: 'the compiler built in this workspace',
	path: 'compiler from PATH',
	install: 'installed compiler',
	extension: 'the compiler built beside this extension'
};

function reportLookupNotice(notice: LookupNotice): void {
	switch (notice.kind) {
		case 'settingPointsAtNothing':
			// ⚠ A SETTING THAT POINTS AT NOTHING IS SAID OUT LOUD rather than skipped. Someone who set it
			// meant it, and silently searching elsewhere hides a typo behind a working editor.
			vscode.window.showWarningMessage(`maxon.serverPath points at nothing: ${notice.configured}`);
			return;
		case 'checkoutHasNoBuild':
			log(`${notice.checkout} is a Maxon checkout with no compiler built at ${notice.slot}; build it with 'maxon run build'. Using another compiler meanwhile.`);
			return;
		default:
			throw new Error(`reportLookupNotice: unhandled notice ${JSON.stringify(notice)}`);
	}
}

/** Where the install scripts put Maxon: `MAXON_INSTALL`, else `~/.maxon`. */
function installRoot(): string {
	return process.env.MAXON_INSTALL || path.join(os.homedir(), '.maxon');
}

/** `maxon` on PATH, or '' — asked of the OS rather than by walking PATH ourselves. */
async function findOnPath(): Promise<string> {
	const probe = isWindows ? 'where' : 'which';
	return new Promise(resolve => {
		cp.execFile(probe, ['maxon'], (err, stdout) => {
			if (err) {
				resolve('');
				return;
			}
			const first = stdout.split(/\r?\n/).map(l => l.trim()).filter(Boolean)[0] ?? '';
			resolve(first);
		});
	});
}

/**
 * No compiler anywhere: offer to get one, rather than reporting a dead end.
 *
 * ⭐ INSTALL RUNS THE SAME ONE-LINE INSTALLER A PERSON WOULD, for this user, so there is no
 * administrator prompt and nothing to click through. Its output goes to the Maxon output channel, and
 * success is judged by finding the compiler afterwards, in the install directory `findCompiler` searches.
 */
async function offerToInstall(ctx: vscode.ExtensionContext): Promise<string> {
	const Install = 'Install';
	const Locate = 'Locate…';
	const choice = await vscode.window.showErrorMessage(
		'Maxon compiler not found. The extension needs it for diagnostics, completion and formatting.',
		{ modal: true },
		Install,
		Locate
	);

	if (choice === Install) {
		const ok = await vscode.window.withProgress(
			{ location: vscode.ProgressLocation.Notification, title: 'Installing Maxon…' },
			() => runInstaller()
		);
		const found = await findCompiler(ctx);
		if (found) {
			return found;
		}
		vscode.window.showErrorMessage(
			ok
				? 'The Maxon installer finished, but no compiler was found. See the Maxon Language Server output.'
				: 'The Maxon installer failed. See the Maxon Language Server output, or install from https://maxon.dev/install.'
		);
		return '';
	}

	if (choice === Locate) {
		const picked = await vscode.window.showOpenDialog({
			canSelectFiles: true,
			canSelectMany: false,
			openLabel: 'Use this compiler',
			title: 'Select the maxon executable'
		});
		const chosen = picked?.[0]?.fsPath;
		if (chosen) {
			await vscode.workspace.getConfiguration('maxon').update('serverPath', chosen, vscode.ConfigurationTarget.Global);
			return chosen;
		}
	}

	return '';
}

const INSTALL_SH = 'https://maxon.dev/install.sh';
const INSTALL_PS1 = 'https://maxon.dev/install.ps1';

/** Run the platform's install script, streaming its output to the log. True when it reports success. */
async function runInstaller(): Promise<boolean> {
	if (isWindows) {
		// The script-block form, because `irm | iex` leaves the exit code at 0 whatever happened.
		const command = `& ([scriptblock]::Create((irm ${INSTALL_PS1}))); exit $LASTEXITCODE`;
		return (await runLogged('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-Command', command])) === 0;
	}

	// Fetched first and piped second, so a failed download is a failure rather than an empty script
	// that `sh` runs successfully.
	const script = await new Promise<string>(resolve => {
		cp.execFile('curl', ['--proto', '=https', '--tlsv1.2', '-fsSL', INSTALL_SH], { maxBuffer: 1 << 20 }, (err, stdout) => {
			if (err) {
				log(`Could not download ${INSTALL_SH}: ${err.message}`);
				resolve('');
				return;
			}
			resolve(stdout);
		});
	});
	return script !== '' && (await runLogged('/bin/sh', ['-s'], script)) === 0;
}

function runLogged(command: string, args: string[], stdin?: string): Promise<number> {
	return new Promise(resolve => {
		const child = cp.spawn(command, args, { stdio: ['pipe', 'pipe', 'pipe'] });
		child.stdout.on('data', chunk => log(String(chunk).trimEnd()));
		child.stderr.on('data', chunk => log(String(chunk).trimEnd()));
		child.on('error', err => {
			log(`Could not run ${command}: ${err.message}`);
			resolve(-1);
		});
		child.on('close', code => resolve(code ?? -1));
		child.stdin.end(stdin ?? '');
	});
}

async function relocateCompiler(active: ExtensionState): Promise<void> {
	const lookup = await locateCompiler(searchFor(active.context));
	if (state !== active) {
		return;
	}

	const next = compilerToRestartOn(active.compilerExecutable, lookup);

	if (next !== active.compilerExecutable) {
		log(`The compiler changed: ${active.compilerExecutable} -> ${next}`);
		active.compilerExecutable = next;
	}

	watchCompilers(active);
}

function disposeCompilerWatchers(): void {
	for (const watcher of compilerWatchers) {
		watcher.dispose();
	}
	compilerWatchers = [];
}

// A rebuilt compiler restarts the server: one still running from the renamed-away `.previous`
// answers from the old compiler, and keeps that file on disk until it exits. Every checkout's slot is
// watched, chosen or not, so building an empty slot moves the server onto it.
function watchCompilers(active: ExtensionState): void {
	disposeCompilerWatchers();

	const executables = watchedCompilers(active.compilerExecutable, workspaceFolderPaths(), compilerBinaryName, isMaxonCheckout);
	compilerWatchers = executables.map(executable => {
		const watcher = vscode.workspace.createFileSystemWatcher(
			new vscode.RelativePattern(path.dirname(executable), path.basename(executable))
		);
		watcher.onDidChange(onCompilerFileChanged);
		watcher.onDidCreate(onCompilerFileChanged);
		return watcher;
	});
}

function onCompilerFileChanged(uri: vscode.Uri): void {
	log(`${uri.fsPath} changed, restarting the language server...`);
	restarts.changed();
}

async function reconsiderCompiler(): Promise<void> {
	const active = state;
	if (!active) return;

	try {
		const lookup = await locateCompiler(searchFor(active.context));
		if (state !== active) {
			return;
		}

		for (const notice of lookup.notices) {
			reportLookupNotice(notice);
		}

		if (compilerToRestartOn(active.compilerExecutable, lookup) !== active.compilerExecutable) {
			await restartClient();
			return;
		}

		watchCompilers(active);
	} catch (error) {
		log(`Re-locating the compiler failed: ${error}`);
	}
}

const CompilerChangeDebounceMs = 1000;

const restarts = new RestartCoordinator(
	performRestart,
	CompilerChangeDebounceMs,
	systemTimers,
	error => log(`LSP auto-restart failed: ${error}`)
);

export function restartClient(): Promise<void> {
	return restarts.request();
}

async function performRestart(): Promise<void> {
	const active = state;
	if (!active) {
		throw new Error('Extension not activated yet');
	}

	log('Restarting LSP client...');

	await relocateCompiler(active);

	try {
		log('Stopping existing LSP client');
		await active.client.stop();
		log('LSP client stopped');
	} catch (error) {
		log(`Error stopping client: ${error}`);
	}

	if (state !== active) {
		log('The extension was deactivated during the restart; not starting a new client');
		return;
	}

	active.client = createClient(active.compilerExecutable, active.clientOptions);
	subscribeToClient(active.client);

	try {
		await active.client.start();
		log('LSP client restarted successfully');
	} catch (error) {
		log(`LSP client restart failed: ${error}`);
		throw error;
	}
}

export function registerCompilerIndependentFeatures(ctx: vscode.ExtensionContext): void {
	registerGuarded('the Test Explorer', () => {
		const registered = registerTestControllers(() => state?.compilerExecutable);
		ctx.subscriptions.push(registered.registration);
		unitTests = registered.unitTests;
	});

	registerGuarded('the debugger', () => {
		ctx.subscriptions.push(registerDebugging(() => state?.compilerExecutable));
	});
}

function registerGuarded(feature: string, register: () => void): void {
	try {
		register();
	} catch (error) {
		log(`Failed to register ${feature}: ${error}`);
		void vscode.window.showErrorMessage(`Maxon could not register ${feature}: ${error}`);
	}
}

export async function activate(ctx: vscode.ExtensionContext) {
	const outputChannel = vscode.window.createOutputChannel('Maxon Language Server');
	initLogger(outputChannel);
	log('Maxon extension activating...');

	// Registered before the compiler is looked for, so the Test Explorer and the debugger exist even
	// when none is found and activation returns early; each asks for the compiler when it runs.
	registerCompilerIndependentFeatures(ctx);

	await removeServerCopy(ctx.globalStorageUri.fsPath);

	let compilerExecutable = await findCompiler(ctx);

	// Someone who installs this extension from the marketplace often has no compiler at all, and an
	// error naming directories they have never heard of leaves them nothing to do. See
	// `offerToInstall`.
	if (!compilerExecutable) {
		compilerExecutable = await offerToInstall(ctx);
	}

	if (!compilerExecutable) {
		log('No Maxon compiler found and none installed');
		return;
	}

	log(`Maxon compiler path: ${compilerExecutable}`);

	const clientOptions: LanguageClientOptions = {
		documentSelector: [
			{ scheme: 'file', language: 'maxon', pattern: '**/*.maxon' },
			{ scheme: 'file', language: 'maxon', pattern: '**/*.maxproj' },
			{ scheme: 'file', language: 'maxon', pattern: '**/*.maxtasks' },
			{ scheme: 'file', language: 'maxon', pattern: '**/*.maxtest' },
			{ scheme: 'file', language: 'maxon', pattern: '**/*.test' }
		],
		synchronize: {
			fileEvents: vscode.workspace.createFileSystemWatcher('**/*.{maxon,maxproj,maxtasks,maxtest,test}'),
			configurationSection: 'maxon'
		},
		outputChannel: outputChannel,
		revealOutputChannelOn: RevealOutputChannelOn.Never,
		// Without a handler the client shows a failed `initialize` with `window.showErrorMessage`.
		initializationFailedHandler: (error) => {
			log(`Server initialization failed: ${error instanceof Error ? error.message : JSON.stringify(error)}`);
			return false;
		},
		middleware: {
			provideDocumentFormattingEdits: async (document, options, token, next) => {
				log('Formatting requested for ' + document.uri.toString());

				const config = vscode.workspace.getConfiguration('maxon.formatting');
				const insertSpaces = config.get<boolean>('insertSpaces', false);
				const tabSize = config.get<number>('tabSize', 2);

				const maxonOptions = {
					...options,
					insertSpaces,
					tabSize
				};

				log(`Formatting with insertSpaces=${insertSpaces}, tabSize=${tabSize}`);
				const result = await next(document, maxonOptions, token);
				const edits = result || [];
				log(`Received ${edits.length} edits from server`);

				// `TextEdit.setEndOfLine` would add a `newEol` property LSP does not carry; the formatter
				// already writes LF line endings.
				return edits;
			}
		}
	};

	const client = createClient(compilerExecutable, clientOptions);

	state = {
		client,
		compilerExecutable,
		clientOptions,
		context: ctx
	};

	statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
	statusBarItem.show();
	ctx.subscriptions.push(statusBarItem);
	ctx.subscriptions.push({ dispose: disposeClientSubscriptions });
	ctx.subscriptions.push({ dispose: () => { if (projectRefreshTimer) clearTimeout(projectRefreshTimer); } });
	subscribeToClient(client);

	// Registered before `client.start()` is awaited: the tooltip links to these commands while the
	// server is still starting.
	ctx.subscriptions.push(
		vscode.commands.registerCommand(RestartLanguageServerCommand, async () => {
			log('Restart language server command invoked');
			try {
				await restartClient();
				vscode.window.showInformationMessage('Maxon Language Server restarted successfully');
			} catch (error) {
				log(`Restart command failed: ${error}`);
			}
		}),
		vscode.commands.registerCommand(ShowLanguageServerOutputCommand, () => outputChannel.show(true))
	);

	ctx.subscriptions.push(
		vscode.window.onDidChangeActiveTextEditor(scheduleProjectRefresh),
		vscode.workspace.onDidSaveTextDocument(scheduleProjectRefresh),
		vscode.workspace.onDidOpenTextDocument(scheduleProjectRefresh),
		vscode.workspace.onDidCloseTextDocument(scheduleProjectRefresh)
	);

	try {
		await client.start();
		log('LSP client started successfully');
	} catch (error) {
		log(`LSP client start failed: ${error}`);
	}

	const compilerExplorerProvider = new CompilerExplorerViewProvider(
		ctx.extensionUri,
		() => state?.client
	);
	ctx.subscriptions.push(
		vscode.window.registerWebviewViewProvider(
			CompilerExplorerViewProvider.viewType,
			compilerExplorerProvider
		)
	);

	const compilerExplorerCommand = vscode.commands.registerCommand(
		'maxon.openCompilerExplorer',
		async () => {
			log('Opening Compiler Explorer');
			await vscode.commands.executeCommand(`${CompilerExplorerViewProvider.viewType}.focus`);
		}
	);

	ctx.subscriptions.push(compilerExplorerCommand);

	const generateIRCommand = vscode.commands.registerCommand(
		'maxon.generateIR',
		async (params: { source: string; filename: string; }) => {
			if (!state?.client) {
				throw new Error('Language server not started');
			}
			return state.client.sendRequest('maxon/generateIR', params);
		}
	);
	ctx.subscriptions.push(generateIRCommand);

	watchCompilers(state);
	ctx.subscriptions.push(
		{ dispose: disposeCompilerWatchers },
		{ dispose: () => restarts.dispose() },
		vscode.workspace.onDidChangeWorkspaceFolders(() => void reconsiderCompiler()),
		vscode.workspace.onDidChangeConfiguration(event => {
			if (event.affectsConfiguration('maxon.serverPath')) {
				void reconsiderCompiler();
			}
		})
	);

	log('Maxon extension activated successfully');

	return { client, getClient, getUnitTests: () => unitTests };
}

export function deactivate(): Thenable<void> | undefined {
	log('Maxon extension deactivating...');
	if (!state) {
		return undefined;
	}
	const stopPromise = state.client.stop();
	state = undefined;
	return stopPromise;
}
