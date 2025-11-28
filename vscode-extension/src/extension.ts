import * as path from 'path';
import * as vscode from 'vscode';
import {
	LanguageClient,
	LanguageClientOptions,
	ServerOptions
} from 'vscode-languageclient/node';
import { log, initLogger, getOutputChannel } from './logger';

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
		args: []
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

	// Path to the compiled LSP server executable
	// Since bin/ is in PATH, we can just use the executable name
	serverExecutable = 'maxon-lsp-server.exe';

	// Server options - use simple command form for stdio communication
	const serverOptions: ServerOptions = {
		command: serverExecutable,
		args: []
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

				// Force LF by appending a setEndOfLine edit
				const eolEdit = vscode.TextEdit.setEndOfLine(vscode.EndOfLine.LF);
				log('Appending setEndOfLine(LF) edit');
				return [...edits, eolEdit];
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

	// Add client to subscriptions for cleanup
	context.subscriptions.push(client);
	log('Maxon extension activated successfully');
}

export function deactivate(): Thenable<void> | undefined {
	log('Maxon extension deactivating...');
	if (!client) {
		return undefined;
	}
	return client.stop();
}
