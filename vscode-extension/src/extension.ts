import * as path from 'path';
import * as vscode from 'vscode';
import {
	LanguageClient,
	LanguageClientOptions,
	ServerOptions
} from 'vscode-languageclient/node';
import { log, initLogger, getOutputChannel } from './logger';
import { CompilerExplorerPanel } from './compilerExplorerPanel';

let client: LanguageClient;
let context: vscode.ExtensionContext;
let serverExecutable: string;
let clientOptions: LanguageClientOptions;

export function getClient(): LanguageClient | undefined {
	return client;
}

/**
 * Restart the LSP client. Useful for testing when the client stops unexpectedly.
 */
export async function restartClient(): Promise<void> {
	if (!context) {
		throw new Error('Extension not activated yet');
	}

	log('Restarting LSP client...');

	// Stop existing client if running
	if (client) {
		try {
			log('Stopping existing LSP client');
			await client.stop();
			log('LSP client stopped');
		} catch (error) {
			log(`Error stopping client: ${error}`);
		}
	}

	// Create new client
	const serverOptions: ServerOptions = {
		command: serverExecutable,
		args: ['lsp-server']
	};

	client = new LanguageClient(
		'maxonLanguageServer',
		'Maxon Language Server',
		serverOptions,
		clientOptions
	);

	// Start the client
	try {
		await client.start();
		log('LSP client restarted successfully');
	} catch (error) {
		log(`LSP client restart failed: ${error}`);
		throw error;
	}
}

export async function activate(ctx: vscode.ExtensionContext) {
	context = ctx;

	// Create output channel for debugging
	const outputChannel = vscode.window.createOutputChannel('Maxon Language Server');
	initLogger(outputChannel);
	log('Maxon extension activating...');

	// Path to the Maxon compiler with embedded LSP server
	// Priority: workspace bin directory first, then fall back to extension-relative path
	const fs = require('fs');
	serverExecutable = '';

	// First, try the workspace bin directory
	if (vscode.workspace.workspaceFolders?.length) {
		const workspaceRoot = vscode.workspace.workspaceFolders[0].uri.fsPath;
		const workspaceBin = path.join(workspaceRoot, 'bin', 'maxon.exe');
		if (fs.existsSync(workspaceBin)) {
			serverExecutable = workspaceBin;
			log(`Using Maxon compiler from workspace: ${serverExecutable}`);
		}
	}

	// Fall back to extension-relative path (development mode: ../bin relative to extension folder)
	if (!serverExecutable) {
		const extensionRelative = path.join(ctx.extensionPath, '..', 'bin', 'maxon.exe');
		if (fs.existsSync(extensionRelative)) {
			serverExecutable = extensionRelative;
			log(`Using Maxon compiler relative to extension: ${serverExecutable}`);
		}
	}

	if (!serverExecutable) {
		const msg = 'Could not find maxon.exe in workspace bin/ or extension directory';
		log(msg);
		vscode.window.showErrorMessage(msg);
		return;
	}

	log(`Maxon compiler path: ${serverExecutable}`);

	// Server options - use the embedded LSP server via 'maxon lsp-server' command
	const serverOptions: ServerOptions = {
		command: serverExecutable,
		args: ['lsp-server']
	};

	clientOptions = {
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

	client = new LanguageClient(
		'maxonLanguageServer',
		'Maxon Language Server',
		serverOptions,
		clientOptions
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

	context.subscriptions.push(restartCommand);

	// Register compiler explorer command
	const compilerExplorerCommand = vscode.commands.registerCommand(
		'maxon.openCompilerExplorer',
		() => {
			log('Opening Compiler Explorer');
			if (!client) {
				vscode.window.showErrorMessage('Language server not started. Please wait for activation.');
				return;
			}
			CompilerExplorerPanel.createOrShow(ctx.extensionUri, client);
		}
	);

	context.subscriptions.push(compilerExplorerCommand);

	// Register commands for testing - these allow tests to call LSP methods via VS Code commands
	const generateIRCommand = vscode.commands.registerCommand(
		'maxon.generateIR',
		async (params: { source: string; filename: string; optimize: boolean; }) => {
			if (!client) {
				throw new Error('Language server not started');
			}
			return client.sendRequest('maxon/generateIR', params);
		}
	);
	context.subscriptions.push(generateIRCommand);

	const generateAsmCommand = vscode.commands.registerCommand(
		'maxon.generateAsm',
		async (params: { source: string; filename: string; optimize: boolean; }) => {
			if (!client) {
				throw new Error('Language server not started');
			}
			return client.sendRequest('maxon/generateAsm', params);
		}
	);
	context.subscriptions.push(generateAsmCommand);

	// Add client to subscriptions for cleanup
	context.subscriptions.push(client);
	log('Maxon extension activated successfully');

	// Export the client for testing
	return { client, getClient };
}

export function deactivate(): Thenable<void> | undefined {
	log('Maxon extension deactivating...');
	if (!client) {
		return undefined;
	}
	return client.stop();
}
