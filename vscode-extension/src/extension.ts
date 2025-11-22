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

    // Stop existing client if running
    if (client) {
        try {
            await client.stop();
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

    // Re-attach event handlers
    client.onDidChangeState((event) => {
        log(`Client state changed: ${event.oldState} -> ${event.newState}`);
    });

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
            { scheme: 'file', language: 'maxon', pattern: '**/*.maxon' }
        ],
        synchronize: {
            fileEvents: vscode.workspace.createFileSystemWatcher('**/*.maxon')
        },
        outputChannel: outputChannel,
        middleware: {
            provideDocumentFormattingEdits: async (document, options, token, next) => {
                log('Formatting requested for ' + document.uri.toString());
                const result = await next(document, options, token);
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

    // Log state changes
    client.onDidChangeState((event) => {
        log(`Client state changed: ${event.oldState} -> ${event.newState}`);
    });

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
            try {
                vscode.window.showInformationMessage('Restarting Maxon Language Server...');
                await restartClient();
                vscode.window.showInformationMessage('Maxon Language Server restarted successfully');
            } catch (error) {
                vscode.window.showErrorMessage(`Failed to restart language server: ${error}`);
            }
        }
    );

    context.subscriptions.push(restartCommand);

    // Force LF on save
    context.subscriptions.push(vscode.workspace.onWillSaveTextDocument(e => {
        if (e.document.languageId === 'maxon') {
            log('onWillSaveTextDocument: Enforcing LF line endings');
            const edits: vscode.TextEdit[] = [];

            // 1. Set EOL to LF
            edits.push(vscode.TextEdit.setEndOfLine(vscode.EndOfLine.LF));

            // 2. Check for CRLF in the text and replace if found
            const text = e.document.getText();
            if (text.includes('\r')) {
                log('Document contains CRLF, replacing with LF');
                const newText = text.replace(/\r/g, '');
                const fullRange = new vscode.Range(
                    e.document.positionAt(0),
                    e.document.positionAt(text.length)
                );
                edits.push(vscode.TextEdit.replace(fullRange, newText));
            }

            e.waitUntil(Promise.resolve(edits));
        }
    }));

    // Add client to subscriptions for cleanup
    context.subscriptions.push(client);
}

export function deactivate(): Thenable<void> | undefined {
    if (!client) {
        return undefined;
    }
    return client.stop();
}
