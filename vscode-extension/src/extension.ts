import * as cp from 'child_process';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import * as vscode from 'vscode';
import {
	LanguageClient,
	LanguageClientOptions,
	ServerOptions,
	State
} from 'vscode-languageclient/node';
import { log, initLogger } from './logger';
import { CompilerExplorerViewProvider } from './compilerExplorerPanel';
import { registerTestController } from './testController';

interface ExtensionState {
	client: LanguageClient;
	context: vscode.ExtensionContext;
	serverExecutable: string; // path to maxon-lsp (the copy we actually run)
	sourceExecutable: string; // path to maxon (the original we watch for changes)
	clientOptions: LanguageClientOptions;
}

let state: ExtensionState | undefined;
let statusBarItem: vscode.StatusBarItem | undefined;
let stateSubscription: vscode.Disposable | undefined;

const isWindows = os.platform() === 'win32';
const binaryName = isWindows ? 'maxon.exe' : 'maxon';
const lspBinaryName = isWindows ? 'maxon-lsp.exe' : 'maxon-lsp';

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
let lastProjects: ProjectInfo[] | undefined;
let projectRefreshTimer: NodeJS.Timeout | undefined;

function buildTooltip(): vscode.MarkdownString {
	const md = new vscode.MarkdownString(undefined, true);
	md.isTrusted = false;
	md.supportThemeIcons = true;

	const stateLabel =
		lastClientState === State.Running ? '$(check) Running' :
		lastClientState === State.Starting ? '$(sync~spin) Starting…' :
		'$(error) Stopped';
	md.appendMarkdown(`**Maxon Language Server** — ${stateLabel}\n\n`);

	if (lastClientState !== State.Running) {
		return md;
	}
	if (!lastProjects) {
		md.appendMarkdown('_Loading projects…_');
		return md;
	}
	if (lastProjects.length === 0) {
		md.appendMarkdown('_No projects loaded_');
		return md;
	}

	md.appendMarkdown('**Loaded projects**\n\n');
	for (const p of lastProjects) {
		const kind = p.isSingleFile ? 'file' : 'project';
		const fileText = p.fileCount === 1 ? '1 file' : `${p.fileCount} files`;
		md.appendMarkdown(`- \`${p.rootPath}\` _(${kind}, ${fileText})_\n`);
	}
	return md;
}

function updateStatusBar() {
	if (!statusBarItem) return;
	switch (lastClientState) {
		case State.Running:
			statusBarItem.text = '$(maxon-logo)';
			statusBarItem.backgroundColor = undefined;
			break;
		case State.Starting:
			statusBarItem.text = '$(sync~spin)';
			statusBarItem.backgroundColor = undefined;
			break;
		case State.Stopped:
			statusBarItem.text = '$(maxon-logo)';
			statusBarItem.backgroundColor = new vscode.ThemeColor('statusBarItem.errorBackground');
			break;
	}
	statusBarItem.tooltip = buildTooltip();
}

async function refreshProjects() {
	const client = state?.client;
	if (!client || client.state !== State.Running) {
		lastProjects = undefined;
		updateStatusBar();
		return;
	}
	try {
		const response = await client.sendRequest<ListProjectsResponse>('maxon/listProjects', {});
		lastProjects = response.projects ?? [];
	} catch (error) {
		log(`maxon/listProjects failed: ${error}`);
		lastProjects = [];
	}
	updateStatusBar();
}

function scheduleProjectRefresh() {
	if (projectRefreshTimer) clearTimeout(projectRefreshTimer);
	projectRefreshTimer = setTimeout(() => refreshProjects(), 500);
}

function subscribeToClientState(client: LanguageClient) {
	stateSubscription?.dispose();
	lastClientState = client.state;
	updateStatusBar();
	if (client.state === State.Running) {
		scheduleProjectRefresh();
	}
	stateSubscription = client.onDidChangeState((e) => {
		lastClientState = e.newState;
		if (e.newState !== State.Running) {
			lastProjects = undefined;
		}
		updateStatusBar();
		if (e.newState === State.Running) {
			scheduleProjectRefresh();
		}
	});
}

function sleep(ms: number): Promise<void> {
	return new Promise(resolve => setTimeout(resolve, ms));
}

/**
 * Copy the maxon binary to a separate maxon-lsp binary so the LSP
 * doesn't lock the main compiler executable during builds.
 * Retries a few times since the old LSP process may not have fully exited yet.
 */
async function copyToLsp(maxonPath: string, storageDir: string): Promise<string> {
	// ⛔ THE COPY GOES TO THE EXTENSION'S OWN STORAGE, NEVER BESIDE THE COMPILER. The compiler may live
	// somewhere the user cannot write — a Homebrew prefix, a directory an administrator unpacked — and
	// global storage is writable by definition. It keeps the reason the copy exists at all: not holding
	// the compiler binary open while a build wants to replace it.
	await fs.promises.mkdir(storageDir, { recursive: true });
	await linkStdlib(maxonPath, storageDir);
	const lspPath = path.join(storageDir, lspBinaryName);
	for (let attempt = 0; attempt < 5; attempt++) {
		try {
			await fs.promises.copyFile(maxonPath, lspPath);
			if (!isWindows) {
				await fs.promises.chmod(lspPath, 0o755);
			}
			log(`Copied ${maxonPath} -> ${lspPath}`);
			return lspPath;
		} catch (error) {
			if (attempt < 4) {
				log(`Copy attempt ${attempt + 1} failed, retrying in 500ms...`);
				await sleep(500);
			} else {
				log(`Failed to copy to LSP binary after 5 attempts: ${error}`);
			}
		}
	}
	return lspPath;
}

/**
 * Give the LSP copy the standard library its original reads.
 *
 * ⛔ WITHOUT THIS THE LANGUAGE SERVER HAS NO STANDARD LIBRARY AND SAYS NOTHING. The compiler finds
 * `stdlib/` by walking up from its OWN executable, and the copy lives in global storage, where nothing
 * above it holds one — so every document is `unavailable` and no diagnostic is ever published. A
 * `stdlib` link beside the copy, pointing at the original's, is the first thing that walk finds.
 * A junction on Windows, which needs no privilege; a directory symlink elsewhere.
 */
async function linkStdlib(maxonPath: string, storageDir: string): Promise<void> {
	const target = await stdlibFor(maxonPath);
	if (!target) {
		log(`No stdlib/ above ${maxonPath}; the language server will report nothing`);
		return;
	}

	const link = path.join(storageDir, 'stdlib');
	let existing: fs.Stats | undefined;
	try {
		existing = await fs.promises.lstat(link);
	} catch {
		existing = undefined;
	}

	if (existing) {
		// ⛔ Only a link is ever replaced. Anything else in that place is left alone and reported.
		if (!existing.isSymbolicLink()) {
			log(`${link} exists and is not a link; leaving it`);
			return;
		}
		if (path.resolve(storageDir, await fs.promises.readlink(link)) === path.resolve(target)) {
			return;
		}
		await fs.promises.unlink(link);
	}

	await fs.promises.symlink(target, link, isWindows ? 'junction' : 'dir');
	log(`Linked ${link} -> ${target}`);
}

/** The `stdlib/` the compiler at `exe` reads: the nearest one above it, found the way the compiler finds it. */
async function stdlibFor(exe: string): Promise<string> {
	let dir = path.dirname(await fs.promises.realpath(exe));
	for (;;) {
		const candidate = path.join(dir, 'stdlib');
		try {
			if ((await fs.promises.stat(candidate)).isDirectory()) {
				return candidate;
			}
		} catch {
			// Not here; keep walking up.
		}
		const parent = path.dirname(dir);
		if (parent === dir) {
			return '';
		}
		dir = parent;
	}
}

/**
 * Where the compiler is, in the order a reader would look.
 *
 * ⭐⭐ THE SETTING, THEN `PATH`, THEN THE INSTALL SCRIPT'S DIRECTORY, THEN THIS WORKSPACE'S OWN BUILD.
 * The install directory is searched directly because a VS Code started from the dock or the Start menu
 * does not see the PATH a shell profile sets, and the install script's PATH change reaches only
 * processes started after it.
 *
 * ⚠ The dev fallback is `maxon-bin/.maxon/`, which is where `maxon build` writes. A contributor with a
 * built tree is found with nothing configured, which is what keeps them out of the install flow.
 */
async function findCompiler(ctx: vscode.ExtensionContext): Promise<string> {
	const configured = vscode.workspace.getConfiguration('maxon').get<string>('serverPath')?.trim();
	if (configured) {
		if (await isExecutable(configured)) {
			log(`Using maxon.serverPath: ${configured}`);
			return configured;
		}
		// ⚠ A SETTING THAT POINTS AT NOTHING IS SAID OUT LOUD rather than skipped. Someone who set it
		// meant it, and silently searching elsewhere hides a typo behind a working editor.
		vscode.window.showWarningMessage(`maxon.serverPath points at nothing: ${configured}`);
	}

	const onPath = await findOnPath();
	if (onPath) {
		log(`Using compiler from PATH: ${onPath}`);
		return onPath;
	}

	const installed = path.join(installRoot(), 'bin', binaryName);
	if (await isExecutable(installed)) {
		log(`Using installed compiler: ${installed}`);
		return installed;
	}

	const candidates: string[] = [];
	if (vscode.workspace.workspaceFolders?.length) {
		const root = vscode.workspace.workspaceFolders[0].uri.fsPath;
		candidates.push(path.join(root, 'maxon-bin', '.maxon', binaryName));
	}
	candidates.push(path.join(ctx.extensionPath, '..', 'maxon-bin', '.maxon', binaryName));

	for (const candidate of candidates) {
		if (await isExecutable(candidate)) {
			log(`Using compiler from this workspace: ${candidate}`);
			return candidate;
		}
	}

	return '';
}

async function isExecutable(candidate: string): Promise<boolean> {
	try {
		await fs.promises.access(candidate, fs.constants.X_OK);
		return true;
	} catch {
		return false;
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

/**
 * Restart the LSP client. Copies the latest binary to the LSP copy first.
 */
export async function restartClient(): Promise<void> {
	if (!state) {
		throw new Error('Extension not activated yet');
	}

	log('Restarting LSP client...');

	// Stop existing client if running
	try {
		log('Stopping existing LSP client');
		await state.client.stop();
		log('LSP client stopped');
	} catch (error) {
		log(`Error stopping client: ${error}`);
	}

	// Copy fresh binary to LSP copy
	state.serverExecutable = await copyToLsp(state.sourceExecutable, state.context.globalStorageUri.fsPath);

	// Create new client
	const serverOptions: ServerOptions = {
		command: state.serverExecutable,
		args: ['lsp-server']
	};

	state.client = new LanguageClient(
		'maxonLanguageServer',
		'Maxon Language Server',
		serverOptions,
		state.clientOptions
	);
	subscribeToClientState(state.client);

	// Start the client
	try {
		await state.client.start();
		log('LSP client restarted successfully');
	} catch (error) {
		log(`LSP client restart failed: ${error}`);
		throw error;
	}
}

export async function activate(ctx: vscode.ExtensionContext) {
	// Create output channel for debugging
	const outputChannel = vscode.window.createOutputChannel('Maxon Language Server');
	initLogger(outputChannel);
	log('Maxon extension activating...');

	// Register the spec-test controller first so it doesn't depend on LSP
	// activation succeeding — early returns below would otherwise leave the
	// Test Explorer empty.
	try {
		ctx.subscriptions.push(registerTestController());
	} catch (error) {
		log(`Failed to register test controller: ${error}`);
	}

	let sourceExecutable = await findCompiler(ctx);

	// ⭐ NOT FOUND IS AN OFFER, NOT A DEAD END. Someone who installs this extension from the
	// marketplace has, very often, no compiler at all — and an error message naming directories they
	// have never heard of leaves them with nothing to do. See `offerToInstall`.
	if (!sourceExecutable) {
		sourceExecutable = await offerToInstall(ctx);
	}

	if (!sourceExecutable) {
		log('No Maxon compiler found and none installed');
		return;
	}

	log(`Maxon compiler path: ${sourceExecutable}`);

	// Copy maxon -> maxon-lsp so the LSP doesn't lock the main binary
	const serverExecutable = await copyToLsp(sourceExecutable, ctx.globalStorageUri.fsPath);

	// Server options - use the copied LSP binary
	const serverOptions: ServerOptions = {
		command: serverExecutable,
		args: ['lsp-server']
	};

	const clientOptions: LanguageClientOptions = {
		documentSelector: [
			{ scheme: 'file', language: 'maxon', pattern: '**/*.maxon' },
			{ scheme: 'file', language: 'maxon', pattern: '**/*.test' }
		],
		synchronize: {
			fileEvents: vscode.workspace.createFileSystemWatcher('**/*.{maxon,test}'),
			configurationSection: 'maxon'
		},
		outputChannel: outputChannel,
		middleware: {
			provideDocumentFormattingEdits: async (document, options, token, next) => {
				log('Formatting requested for ' + document.uri.toString());

				// Override options with Maxon-specific settings
				const config = vscode.workspace.getConfiguration('maxon.formatting');
				const insertSpaces = config.get<boolean>('insertSpaces', false);
				const tabSize = config.get<number>('tabSize', 4);

				const maxonOptions = {
					...options,
					insertSpaces,
					tabSize
				};

				log(`Formatting with insertSpaces=${insertSpaces}, tabSize=${tabSize}`);
				const result = await next(document, maxonOptions, token);
				const edits = result || [];
				log(`Received ${edits.length} edits from server`);

				// Note: We can't use TextEdit.setEndOfLine here because it creates an edit
				// with a `newEol` property that's not compatible with the LSP protocol.
				// The formatter already normalizes to LF line endings.
				return edits;
			}
		}
	};

	const client = new LanguageClient(
		'maxonLanguageServer',
		'Maxon Language Server',
		serverOptions,
		clientOptions
	);

	// Populate the extension state
	state = {
		client,
		context: ctx,
		serverExecutable,
		sourceExecutable,
		clientOptions
	};

	// Status bar item showing LSP state. Hover for the loaded project list.
	statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
	statusBarItem.show();
	ctx.subscriptions.push(statusBarItem);
	ctx.subscriptions.push({ dispose: () => stateSubscription?.dispose() });
	ctx.subscriptions.push({ dispose: () => { if (projectRefreshTimer) clearTimeout(projectRefreshTimer); } });
	subscribeToClientState(client);

	// Refresh the loaded-project list when the active editor changes (a new
	// project may have been opened) or when a file is saved (project files
	// may have been added/removed on disk). Both events are cheap to debounce.
	ctx.subscriptions.push(
		vscode.window.onDidChangeActiveTextEditor(scheduleProjectRefresh),
		vscode.workspace.onDidSaveTextDocument(scheduleProjectRefresh),
		vscode.workspace.onDidOpenTextDocument(scheduleProjectRefresh),
		vscode.workspace.onDidCloseTextDocument(scheduleProjectRefresh)
	);

	// Start the client and await completion
	try {
		await client.start();
		log('LSP client started successfully');
	} catch (error) {
		log(`LSP client start failed: ${error}`);
		vscode.window.showErrorMessage(`Maxon Language Server failed to start: ${error}`);
	}

	// Register restart command
	const restartCommand = vscode.commands.registerCommand(
		'maxon.restartLanguageServer',
		async () => {
			log('Restart language server command invoked');
			try {
				await restartClient();
				vscode.window.showInformationMessage('Maxon Language Server restarted successfully');
			} catch (error) {
				log(`Restart command failed: ${error}`);
				vscode.window.showErrorMessage(`Failed to restart language server: ${error}`);
			}
		}
	);

	ctx.subscriptions.push(restartCommand);

	// Register the compiler explorer webview view provider (lives in the activity bar)
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

	// Command opens the activity bar view
	const compilerExplorerCommand = vscode.commands.registerCommand(
		'maxon.openCompilerExplorer',
		async () => {
			log('Opening Compiler Explorer');
			await vscode.commands.executeCommand(`${CompilerExplorerViewProvider.viewType}.focus`);
		}
	);

	ctx.subscriptions.push(compilerExplorerCommand);

	// Register commands for testing - these allow tests to call LSP methods via VS Code commands
	const generateIRCommand = vscode.commands.registerCommand(
		'maxon.generateIR',
		async (params: { source: string; filename: string; optimize: boolean; }) => {
			if (!state?.client) {
				throw new Error('Language server not started');
			}
			return state.client.sendRequest('maxon/generateIR', params);
		}
	);
	ctx.subscriptions.push(generateIRCommand);

	const generateAsmCommand = vscode.commands.registerCommand(
		'maxon.generateAsm',
		async (params: { source: string; filename: string; optimize: boolean; }) => {
			if (!state?.client) {
				throw new Error('Language server not started');
			}
			return state.client.sendRequest('maxon/generateAsm', params);
		}
	);
	ctx.subscriptions.push(generateAsmCommand);

	// Watch the maxon binary for changes — when it's rebuilt, restart the LSP with the new copy
	const serverDir = path.dirname(sourceExecutable);
	const serverFile = path.basename(sourceExecutable);
	const watcher = vscode.workspace.createFileSystemWatcher(
		new vscode.RelativePattern(serverDir, serverFile)
	);
	let restartDebounce: ReturnType<typeof setTimeout> | undefined;
	const autoRestart = (uri: vscode.Uri) => {
		// Debounce: the build may produce multiple file events (rename old, copy new)
		if (restartDebounce) clearTimeout(restartDebounce);
		restartDebounce = setTimeout(async () => {
			log(`${binaryName} changed (${uri.fsPath}), restarting LSP with new binary...`);
			try {
				await restartClient();
				log('LSP auto-restarted after binary change');
			} catch (error) {
				log(`LSP auto-restart failed: ${error}`);
			}
		}, 1000);
	};
	watcher.onDidChange(autoRestart);
	watcher.onDidCreate(autoRestart);
	ctx.subscriptions.push(watcher);

	// Add client to subscriptions for cleanup
	ctx.subscriptions.push(client);

	log('Maxon extension activated successfully');

	// Export the client for testing
	return { client, getClient };
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
