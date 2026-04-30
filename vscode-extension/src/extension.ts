import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import * as vscode from 'vscode';
import {
	LanguageClient,
	LanguageClientOptions,
	ServerOptions
} from 'vscode-languageclient/node';
import { log, initLogger } from './logger';
import { CompilerExplorerPanel } from './compilerExplorerPanel';
import { registerTestController } from './testController';

interface ExtensionState {
	client: LanguageClient;
	context: vscode.ExtensionContext;
	serverExecutable: string; // path to maxon-lsp (the copy we actually run)
	sourceExecutable: string; // path to maxon (the original we watch for changes)
	clientOptions: LanguageClientOptions;
}

let state: ExtensionState | undefined;

const isWindows = os.platform() === 'win32';
const binaryName = isWindows ? 'maxon.exe' : 'maxon';
const lspBinaryName = isWindows ? 'maxon-lsp.exe' : 'maxon-lsp';

export function getClient(): LanguageClient | undefined {
	return state?.client;
}

function sleep(ms: number): Promise<void> {
	return new Promise(resolve => setTimeout(resolve, ms));
}

/**
 * Copy the maxon binary to a separate maxon-lsp binary so the LSP
 * doesn't lock the main compiler executable during builds.
 * Retries a few times since the old LSP process may not have fully exited yet.
 */
async function copyToLsp(maxonPath: string): Promise<string> {
	const dir = path.dirname(maxonPath);
	const lspPath = path.join(dir, lspBinaryName);
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
	state.serverExecutable = await copyToLsp(state.sourceExecutable);

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

	// Find the maxon binary — this is the source we watch for changes
	let sourceExecutable = '';

	// First, try the workspace bin directory
	if (vscode.workspace.workspaceFolders?.length) {
		const workspaceRoot = vscode.workspace.workspaceFolders[0].uri.fsPath;
		const workspaceBin = path.join(workspaceRoot, 'bin', binaryName);
		try {
			await fs.promises.access(workspaceBin);
			sourceExecutable = workspaceBin;
			log(`Using Maxon compiler from workspace: ${sourceExecutable}`);
		} catch {
			// Not found in workspace bin, will try fallback
		}
	}

	// Fall back to extension-relative path (development mode: ../bin relative to extension folder)
	if (!sourceExecutable) {
		const extensionRelative = path.join(ctx.extensionPath, '..', 'bin', binaryName);
		try {
			await fs.promises.access(extensionRelative);
			sourceExecutable = extensionRelative;
			log(`Using Maxon compiler relative to extension: ${sourceExecutable}`);
		} catch {
			// Not found relative to extension either
		}
	}

	if (!sourceExecutable) {
		const msg = `Could not find ${binaryName} in workspace bin/ or extension directory`;
		log(msg);
		vscode.window.showErrorMessage(msg);
		return;
	}

	log(`Maxon compiler path: ${sourceExecutable}`);

	// Copy maxon -> maxon-lsp so the LSP doesn't lock the main binary
	const serverExecutable = await copyToLsp(sourceExecutable);

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

	// Register compiler explorer command
	const compilerExplorerCommand = vscode.commands.registerCommand(
		'maxon.openCompilerExplorer',
		() => {
			log('Opening Compiler Explorer');
			if (!state?.client) {
				vscode.window.showErrorMessage('Language server not started. Please wait for activation.');
				return;
			}
			CompilerExplorerPanel.createOrShow(ctx.extensionUri, state.client);
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
