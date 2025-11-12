import * as assert from 'assert';
import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import { getClient } from '../../extension';
import { State } from 'vscode-languageclient';

suite('Stdlib Completion Tests', () => {
    let document: vscode.TextDocument;
    const testTimeout = 30000; // 30 seconds for LSP to initialize

    suiteSetup(async function() {
        this.timeout(testTimeout);
        
        // Ensure extension is activated
        const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
        if (ext && !ext.isActive) {
            await ext.activate();
        }
        
        // Wait for LSP client to be in Running state
        console.log('Waiting for LSP server to initialize...');
        const client = getClient();
        if (client) {
            let attempts = 0;
            while (client.state !== State.Running && attempts < 50) {
                await new Promise(resolve => setTimeout(resolve, 200));
                attempts++;
            }
            console.log(`LSP client state: ${State[client.state]} after ${attempts * 200}ms`);
        } else {
            console.log('WARNING: Could not get LSP client reference');
            // Fallback to time-based wait
            await new Promise(resolve => setTimeout(resolve, 10000));
        }
    });

    setup(async function() {
        this.timeout(testTimeout);
        
        // Create a temporary test file
        const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
        assert.ok(workspaceFolder, 'Workspace folder should be available');
        
        const testFilePath = path.join(workspaceFolder.uri.fsPath, 'test_stdlib.maxon');
        
        // Create test file with basic content
        fs.writeFileSync(testFilePath, 'function main() int\n    \nend \'main\'', 'utf-8');
        
        // Open the document
        document = await vscode.workspace.openTextDocument(testFilePath);
        await vscode.window.showTextDocument(document);
        
        // Check document language ID
        console.log('Document language ID:', document.languageId);
        console.log('Document URI:', document.uri.toString());
    });

    teardown(async () => {
        // Close all editors
        await vscode.commands.executeCommand('workbench.action.closeAllEditors');
        
        // Clean up test file
        if (document) {
            try {
                fs.unlinkSync(document.uri.fsPath);
            } catch (e) {
                // Ignore errors
            }
        }
    });

    test('Should provide stdlib function completions (if LSP server running)', async function() {
        this.timeout(testTimeout);
        
        const position = new vscode.Position(1, 4); // Inside function body
        
        // Request completions
        const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
            'vscode.executeCompletionItemProvider',
            document.uri,
            position
        );
        
        assert.ok(completions, 'Completions should be returned');
        
        // Debug: log available completions
        console.log(`Found ${completions.items.length} completions`);
        const labels = completions.items.map(item => item.label).slice(0, 20);
        console.log('First 20 completions:', labels);
        
        // Check if format_int_array is in the completions
        const hasFormatIntArray = completions.items.some(
            item => item.label === 'format_int_array'
        );
        
        assert.ok(hasFormatIntArray, 'Should include format_int_array from stdlib');
    });

    test('Stdlib function completion should have correct kind', async function() {
        this.timeout(testTimeout);
        
        const position = new vscode.Position(1, 4);
        const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
            'vscode.executeCompletionItemProvider',
            document.uri,
            position
        );
        
        const formatIntArray = completions.items.find(
            item => item.label === 'format_int_array'
        );
        
        assert.ok(formatIntArray, 'format_int_array should be found');
        assert.strictEqual(
            formatIntArray.kind,
            vscode.CompletionItemKind.Function,
            'format_int_array should be a Function'
        );
    });

    test('Stdlib function completion should have detail with signature', async function() {
        this.timeout(testTimeout);
        
        const position = new vscode.Position(1, 4);
        const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
            'vscode.executeCompletionItemProvider',
            document.uri,
            position
        );
        
        const formatIntArray = completions.items.find(
            item => item.label === 'format_int_array'
        );
        
        assert.ok(formatIntArray, 'format_int_array should be found');
        assert.ok(formatIntArray.detail, 'Should have detail field');
        
        // Check that signature contains expected elements
        const detail = formatIntArray.detail as string;
        assert.ok(detail.includes('value int'), 'Detail should include parameter "value int"');
        assert.ok(detail.includes('buffer'), 'Detail should include parameter "buffer"');
        assert.ok(detail.includes('[12]char'), 'Detail should include array type "[12]char"');
    });

    test('Stdlib function completion should have documentation', async function() {
        this.timeout(testTimeout);
        
        const position = new vscode.Position(1, 4);
        const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
            'vscode.executeCompletionItemProvider',
            document.uri,
            position
        );
        
        const formatIntArray = completions.items.find(
            item => item.label === 'format_int_array'
        );
        
        assert.ok(formatIntArray, 'format_int_array should be found');
        assert.ok(formatIntArray.documentation, 'Should have documentation');
    });

    test('Should provide hover information for stdlib functions', async function() {
        this.timeout(testTimeout);
        
        // Add stdlib function call to document
        const edit = new vscode.WorkspaceEdit();
        edit.insert(document.uri, new vscode.Position(1, 4), 'format_int_array');
        await vscode.workspace.applyEdit(edit);
        await document.save();
        
        // Wait for LSP to process
        await new Promise(resolve => setTimeout(resolve, 500));
        
        const position = new vscode.Position(1, 10); // Middle of "format_int_array"
        
        const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
            'vscode.executeHoverProvider',
            document.uri,
            position
        );
        
        assert.ok(hovers && hovers.length > 0, 'Should return hover information');
        
        const hoverText = hovers[0].contents.map(c => 
            typeof c === 'string' ? c : c.value
        ).join('\n');
        
        assert.ok(
            hoverText.includes('stdlib::fmt::format_int_array'),
            'Hover should show qualified function name'
        );
        assert.ok(
            hoverText.includes('function'),
            'Hover should indicate it is a function'
        );
    });

    test('Should include keyword completions alongside stdlib', async function() {
        this.timeout(testTimeout);
        
        const position = new vscode.Position(1, 4);
        const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
            'vscode.executeCompletionItemProvider',
            document.uri,
            position
        );
        
        // Should have both stdlib functions and keywords
        const hasStdlibFunc = completions.items.some(
            item => item.label === 'format_int_array'
        );
        const hasKeyword = completions.items.some(
            item => item.label === 'var' || item.label === 'let'
        );
        
        assert.ok(hasStdlibFunc, 'Should include stdlib functions');
        assert.ok(hasKeyword, 'Should include keywords');
    });

    test('Should provide completions in different positions', async function() {
        this.timeout(testTimeout);
        
        // Test at beginning of line
        const pos1 = new vscode.Position(1, 0);
        const completions1 = await vscode.commands.executeCommand<vscode.CompletionList>(
            'vscode.executeCompletionItemProvider',
            document.uri,
            pos1
        );
        
        assert.ok(completions1.items.length > 0, 'Should provide completions at line start');
        
        // Test after whitespace
        const pos2 = new vscode.Position(1, 4);
        const completions2 = await vscode.commands.executeCommand<vscode.CompletionList>(
            'vscode.executeCompletionItemProvider',
            document.uri,
            pos2
        );
        
        assert.ok(completions2.items.length > 0, 'Should provide completions after whitespace');
    });
});

suite('Stdlib Error Handling Tests', () => {
    test('Should handle missing stdlib directory gracefully', async function() {
        this.timeout(10000);
        
        // Even if stdlib is not found, extension should work
        const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
        assert.ok(ext, 'Extension should load');
        
        // Extension should still be active
        if (ext && !ext.isActive) {
            await ext.activate();
        }
        assert.ok(ext?.isActive !== false, 'Extension should activate');
    });
});
