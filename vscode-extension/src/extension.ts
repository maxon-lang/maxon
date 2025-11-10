import * as path from 'path';
import * as vscode from 'vscode';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    TransportKind
} from 'vscode-languageclient/node';

let client: LanguageClient;

export function activate(context: vscode.ExtensionContext) {
    // Path to the compiled LSP server executable
    // Adjust this path based on where you build the server
    const serverExecutable = path.join(
        context.extensionPath, 
        '..', 
        'build', 
        'maxon-lsp.exe'  // or 'maxon-lsp' on Linux/Mac
    );

    const serverOptions: ServerOptions = {
        command: serverExecutable,
        transport: TransportKind.stdio
    };

    const clientOptions: LanguageClientOptions = {
        documentSelector: [{ scheme: 'file', language: 'maxon' }],
        synchronize: {
            fileEvents: vscode.workspace.createFileSystemWatcher('**/.maxon')
        }
    };

    client = new LanguageClient(
        'maxonLanguageServer',
        'Maxon Language Server',
        serverOptions,
        clientOptions
    );

    client.start();
}

export function deactivate(): Thenable<void> | undefined {
    if (!client) {
        return undefined;
    }
    return client.stop();
}
