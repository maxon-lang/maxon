import * as path from 'path';
import * as vscode from 'vscode';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions
} from 'vscode-languageclient/node';

let client: LanguageClient;
let outputChannel: vscode.OutputChannel;

export function getClient(): LanguageClient | undefined {
    return client;
}

export async function activate(context: vscode.ExtensionContext) {
    // Create output channel for debugging
    outputChannel = vscode.window.createOutputChannel('Maxon LSP Debug');
    outputChannel.show(true);
    outputChannel.appendLine('[Extension] === ACTIVATION STARTED ===');
    
    // Path to the compiled LSP server executable
    // Look in project_root/bin/ directory (created by CMake post-build step)
    const serverExecutable = path.join(
        context.extensionPath, 
        '..', 
        'bin',
        'maxon-lsp.exe'  // or 'maxon-lsp' on Linux/Mac
    );

    outputChannel.appendLine(`[Extension] Server executable path: ${serverExecutable}`);
    outputChannel.appendLine(`[Extension] Server exists: ${require('fs').existsSync(serverExecutable)}`);
    
    // Server options - use simple command form for stdio communication
    const serverOptions: ServerOptions = {
        command: serverExecutable,
        args: []
    };

    const clientOptions: LanguageClientOptions = {
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
        outputChannel.appendLine(`[Extension] Client state changed: ${event.oldState} -> ${event.newState}`);
    });

    outputChannel.appendLine('[Extension] Starting LSP client...');
    
    // Start the client and await completion
    try {
        await client.start();
        outputChannel.appendLine('[Extension] LSP client started and initialized successfully');
    } catch (error) {
        outputChannel.appendLine(`[Extension] LSP client start failed: ${error}`);
    }
    
    // Log document events
    vscode.workspace.onDidOpenTextDocument((doc) => {
        outputChannel.appendLine(`[Extension] Document opened: ${doc.uri.toString()}, language: ${doc.languageId}`);
    });
    
    vscode.workspace.onDidChangeTextDocument((event) => {
        //outputChannel.appendLine(`[Extension] Document changed: ${event.document.uri.toString()}`);
    });
    
    // Add client to subscriptions for cleanup
    context.subscriptions.push(client);
    
    outputChannel.appendLine('[Extension] === ACTIVATION COMPLETE ===');
}

export function deactivate(): Thenable<void> | undefined {
    if (!client) {
        return undefined;
    }
    return client.stop();
}
