import * as assert from 'assert';
import * as vscode from 'vscode';
import * as path from 'path';
import { getLogs, clearLogs } from '../../logger';
import { getClient, restartClient } from '../../extension';
import { State } from 'vscode-languageclient';

suite('Diagnostics Tests', () => {
    let testFileUri: vscode.Uri;
    let document: vscode.TextDocument;

    setup(async () => {
        // Clear logs before each test for isolation
        clearLogs();
        
        // Ensure LSP client is running
        let client = getClient();
        if (!client || (client.state as number) !== 2 /* Running */) {
            console.log(`LSP Client state: ${client?.state ?? 'undefined'}, restarting...`);
            try {
                await restartClient();
                client = getClient();
                console.log(`LSP Client restarted, new state: ${client?.state}`);
            } catch (error) {
                console.error(`Failed to restart LSP client: ${error}`);
                throw new Error(`LSP Client not available for diagnostic tests: ${error}`);
            }
        } else {
            console.log(`LSP Client already running (state: ${client.state})`);
        }
        
        // Create a temporary test file
        const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
        if (!workspaceFolder) {
            throw new Error('No workspace folder found');
        }
        
        const testFilePath = path.join(workspaceFolder.uri.fsPath, 'temp', 'test-diagnostics.maxon');
        testFileUri = vscode.Uri.file(testFilePath);
    });

    teardown(async () => {
        // Clean up: close the document
        if (document) {
            await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
        }
    });

    test('Should report warning for unused variable', async function() {
        // Create a document with an unused variable
        const content = `function main() int
    var unused = 5
    return 0
end 'main'
`;
        
        // Create and open the document
        await vscode.workspace.fs.writeFile(testFileUri, Buffer.from(content, 'utf8'));
        document = await vscode.workspace.openTextDocument(testFileUri);
        const editor = await vscode.window.showTextDocument(document);
        
        // Trigger a document change to ensure LSP analyzes it
        await editor.edit(editBuilder => {
            editBuilder.insert(new vscode.Position(0, 0), '');
        });
        
        // Wait for diagnostics to be published - poll for them
        let diagnostics: vscode.Diagnostic[] = [];
        const maxAttempts = 10;
        for (let i = 0; i < maxAttempts; i++) {
            await new Promise(resolve => setTimeout(resolve, 100));
            diagnostics = vscode.languages.getDiagnostics(testFileUri);
            if (diagnostics.length > 0) {
                console.log(`Got ${diagnostics.length} diagnostics after ${(i+1)*300}ms`);
                break;
            }
        }
        
        console.log(`Final diagnostic count: ${diagnostics.length}`);
        if (diagnostics.length > 0) {
            console.log('Diagnostics:', diagnostics.map(d => `[${d.severity}] ${d.message}`).join('\n'));
        }
        
        // Print extension logs to help debug
        const logs = getLogs();
        console.log('\n=== Extension Logs ===');
        logs.forEach(log => console.log(log));
        console.log('======================\n');
        
        // Should have at least one diagnostic
        assert.ok(diagnostics.length > 0, `Should have diagnostics (got ${diagnostics.length})`);
        
        // Find the unused variable warning
        const unusedWarning = diagnostics.find(d => 
            d.message.includes('never used') && 
            d.message.includes("'unused'")
        );
        
        assert.ok(unusedWarning, `Should have warning for unused variable. Got: ${diagnostics.map(d => d.message).join(', ')}`);
        assert.strictEqual(unusedWarning.severity, vscode.DiagnosticSeverity.Warning, 
            'Unused variable should be a warning, not an error');
    });

    test('Should report multiple unused variables', async function() {
        const content = `function main() int
    var unused1 = 5
    var unused2 = 10
    var used = 15
    print(used)
    return 0
end 'main'
`;
        
        await vscode.workspace.fs.writeFile(testFileUri, Buffer.from(content, 'utf8'));
        document = await vscode.workspace.openTextDocument(testFileUri);
        await vscode.window.showTextDocument(document);
        
        // Wait for diagnostics - poll for them
        let diagnostics: vscode.Diagnostic[] = [];
        const maxAttempts = 20;
        for (let i = 0; i < maxAttempts; i++) {
            await new Promise(resolve => setTimeout(resolve, 100));
            diagnostics = vscode.languages.getDiagnostics(testFileUri);
            if (diagnostics.length >= 2) {
                break;
            }
        }
        
        // Print extension logs to help debug
        const logs = getLogs();
        console.log('\n=== Extension Logs ===');
        logs.forEach(log => console.log(log));
        console.log('======================\n');
        
        // Should have warnings for both unused1 and unused2
        const unusedWarnings = diagnostics.filter(d => 
            d.message.includes('never used') &&
            d.severity === vscode.DiagnosticSeverity.Warning
        );
        
        assert.strictEqual(unusedWarnings.length, 2, 
            `Should have 2 unused variable warnings (got ${unusedWarnings.length}). Diagnostics: ${diagnostics.map(d => d.message).join(', ')}`);
        
        const hasUnused1 = unusedWarnings.some(d => d.message.includes("'unused1'"));
        const hasUnused2 = unusedWarnings.some(d => d.message.includes("'unused2'"));
        
        assert.ok(hasUnused1, 'Should warn about unused1');
        assert.ok(hasUnused2, 'Should warn about unused2');
    });

    test('Should distinguish warnings from errors', async function() {
        // Create a document with both an error and a warning
        const content = `function main() int
    var unused = 5
    print(undefined)
    return 0
end 'main'
`;
        
        await vscode.workspace.fs.writeFile(testFileUri, Buffer.from(content, 'utf8'));
        document = await vscode.workspace.openTextDocument(testFileUri);
        const editor = await vscode.window.showTextDocument(document);
        
        // Trigger a document change to ensure LSP analyzes the new content
        await editor.edit(editBuilder => {
            editBuilder.insert(new vscode.Position(0, 0), '');
        });
        
        // Wait for diagnostics - poll for them
        let diagnostics: vscode.Diagnostic[] = [];
        const maxAttempts = 20;
        for (let i = 0; i < maxAttempts; i++) {
            await new Promise(resolve => setTimeout(resolve, 100));
            diagnostics = vscode.languages.getDiagnostics(testFileUri);
            // Check if we have the right diagnostics (should include "Undefined")
            const hasUndefined = diagnostics.some(d => d.message.includes('Undefined'));
            if (diagnostics.length >= 2 && hasUndefined) {
                break;
            }
        }
        
        // Print extension logs to help debug
        const logs = getLogs();
        console.log('\n=== Extension Logs ===');
        logs.forEach(log => console.log(log));
        console.log('======================\n');
        
        // Should have both errors and warnings
        const errors = diagnostics.filter(d => d.severity === vscode.DiagnosticSeverity.Error);
        const warnings = diagnostics.filter(d => d.severity === vscode.DiagnosticSeverity.Warning);
        
        assert.ok(errors.length > 0, 
            `Should have at least one error (got ${errors.length}). All diagnostics: ${diagnostics.map(d => `[${d.severity}] ${d.message}`).join(', ')}`);
        assert.ok(warnings.length > 0, 
            `Should have at least one warning (got ${warnings.length}). All diagnostics: ${diagnostics.map(d => `[${d.severity}] ${d.message}`).join(', ')}`);
        
        // The undefined variable should be an error
        const undefinedError = errors.find(d => 
            d.message.includes('Undefined') || d.message.includes('undefined')
        );
        assert.ok(undefinedError, 'Should have error for undefined variable');
        
        // The unused variable should be a warning
        const unusedWarning = warnings.find(d => 
            d.message.includes('never used')
        );
        assert.ok(unusedWarning, 'Should have warning for unused variable');
    });

    test('Should not report error for stdlib function call', async function() {
        // Create a document that uses stdlib function
        const content = `function main() int
    var buffer [12]char = 0
    var length = format_int_array(42, buffer)
    return length
end 'main'
`;
        
        await vscode.workspace.fs.writeFile(testFileUri, Buffer.from(content, 'utf8'));
        document = await vscode.workspace.openTextDocument(testFileUri);
        await vscode.window.showTextDocument(document);
        
        // Wait for diagnostics
        let diagnostics: vscode.Diagnostic[] = [];
        const maxAttempts = 20;
        for (let i = 0; i < maxAttempts; i++) {
            await new Promise(resolve => setTimeout(resolve, 100));
            diagnostics = vscode.languages.getDiagnostics(testFileUri);
            // Wait for analysis to complete (should have no errors about format_int_array)
            const hasFormatIntArrayError = diagnostics.some(d => 
                d.message.includes('format_int_array') && 
                d.message.includes('Undefined')
            );
            if (!hasFormatIntArrayError) {
                break;
            }
        }
        
        console.log(`Diagnostics: ${diagnostics.map(d => `[${d.severity}] ${d.message}`).join('\n')}`);
        
        // Should NOT have "Undefined function" error for format_int_array
        const undefinedFunctionError = diagnostics.find(d => 
            d.message.includes('Undefined function') &&
            d.message.includes('format_int_array')
        );
        
        assert.strictEqual(undefinedFunctionError, undefined,
            `Should not report stdlib function as undefined. Got: ${diagnostics.map(d => d.message).join(', ')}`);
    });

    test('Should report error for stdlib function with wrong argument count', async function() {
        // format_int_array expects 2 arguments, we provide 1
        const content = `function main() int
    var buffer = [12]char
    var length = format_int_array(42)
    return length
end 'main'
`;
        
        await vscode.workspace.fs.writeFile(testFileUri, Buffer.from(content, 'utf8'));
        document = await vscode.workspace.openTextDocument(testFileUri);
        const editor = await vscode.window.showTextDocument(document);
        
        // Trigger analysis
        await editor.edit(editBuilder => {
            editBuilder.insert(new vscode.Position(0, 0), '');
        });
        
        // Wait for diagnostics
        let diagnostics: vscode.Diagnostic[] = [];
        const maxAttempts = 20;
        for (let i = 0; i < maxAttempts; i++) {
            await new Promise(resolve => setTimeout(resolve, 100));
            diagnostics = vscode.languages.getDiagnostics(testFileUri);
            const hasArgError = diagnostics.some(d => 
                d.message.includes('Expected 2 arguments') ||
                d.message.includes('argument')
            );
            if (hasArgError) {
                break;
            }
        }
        
        console.log(`Diagnostics: ${diagnostics.map(d => `[${d.severity}] ${d.message}`).join('\n')}`);
        
        // Should have error about wrong argument count
        const argCountError = diagnostics.find(d => 
            d.message.includes('Expected 2 arguments') ||
            (d.message.includes('argument') && d.severity === vscode.DiagnosticSeverity.Error)
        );
        
        assert.ok(argCountError,
            `Should report error for wrong argument count. Got: ${diagnostics.map(d => d.message).join(', ')}`);
        assert.strictEqual(argCountError.severity, vscode.DiagnosticSeverity.Error,
            'Wrong argument count should be an error');
    });

    test('Should clear diagnostics when file is closed', async function() {
        // Create a document with errors
        const content = `function main() int
    var unused = 5
    print(undefined)
    return 0
end 'main'
`;
        
        await vscode.workspace.fs.writeFile(testFileUri, Buffer.from(content, 'utf8'));
        document = await vscode.workspace.openTextDocument(testFileUri);
        const editor = await vscode.window.showTextDocument(document);
        
        // Wait for diagnostics to appear
        let diagnostics: vscode.Diagnostic[] = [];
        const maxAttempts = 20;
        for (let i = 0; i < maxAttempts; i++) {
            await new Promise(resolve => setTimeout(resolve, 100));
            diagnostics = vscode.languages.getDiagnostics(testFileUri);
            if (diagnostics.length > 0) {
                break;
            }
        }
        
        console.log(`Initial diagnostics: ${diagnostics.length}`);
        assert.ok(diagnostics.length > 0, 'Should have initial diagnostics');
        
        // Get the language client and manually send didClose notification
        const client = getClient();
        if (client) {
            // Send didClose notification directly
            await client.sendNotification('textDocument/didClose', {
                textDocument: {
                    uri: testFileUri.toString()
                }
            });
        }
        
        // Wait for the close notification to be processed
        await new Promise(resolve => setTimeout(resolve, 100));
        
        // Check that diagnostics are cleared
        diagnostics = vscode.languages.getDiagnostics(testFileUri);
        console.log(`Diagnostics after didClose: ${diagnostics.length}`);
        
        // Close editor and delete file for cleanup
        await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
        await vscode.workspace.fs.delete(testFileUri);
        
        assert.strictEqual(diagnostics.length, 0,
            `Diagnostics should be cleared when file is closed. Still have: ${diagnostics.map(d => d.message).join(', ')}`);
    });
});
