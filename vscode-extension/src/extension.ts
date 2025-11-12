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
        documentSelector: [{ scheme: 'file', language: 'maxon' }],
        synchronize: {
            fileEvents: vscode.workspace.createFileSystemWatcher('**/.maxon')
        },
        outputChannel: outputChannel
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
    
    // Add client to subscriptions for cleanup
    context.subscriptions.push(client);
}

export function deactivate(): Thenable<void> | undefined {
    if (!client) {
        return undefined;
    }
    return client.stop();
}
