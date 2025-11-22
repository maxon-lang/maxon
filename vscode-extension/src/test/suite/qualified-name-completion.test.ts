import * as assert from 'assert';
import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import { getClient } from '../../extension';
import { State } from 'vscode-languageclient';

suite('Qualified Name Completion Tests', () => {
    let document: vscode.TextDocument;

    suiteSetup(async function() {
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
                await new Promise(resolve => setTimeout(resolve, 100));
                attempts++;
            }
            console.log(`LSP client state: ${State[client.state]} after ${attempts * 200}ms`);
		} else {
			throw new Error('LSP client not found');
        }
    });

    setup(async function() {
        // Create a temporary test file
        const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
        assert.ok(workspaceFolder, 'Workspace folder should be available');
        
        const testFilePath = path.join(workspaceFolder.uri.fsPath, 'temp', 'test_qualified_name.maxon');
        
        // Create test file with basic content
        fs.writeFileSync(testFilePath, 'function main() int\n    \nend \'main\'', 'utf-8');
        
        // Open the document
        document = await vscode.workspace.openTextDocument(testFilePath);
        await vscode.window.showTextDocument(document);
        
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

    test('Should suggest "stdlib" when typing "std"', async function() {
        // Edit document to contain "std"
        const editor = vscode.window.activeTextEditor;
        assert.ok(editor, 'Editor should be active');
        
        await editor.edit(editBuilder => {
            editBuilder.insert(new vscode.Position(1, 4), 'std');
        });
        
        const position = new vscode.Position(1, 7); // After "std"
        
        const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
            'vscode.executeCompletionItemProvider',
            document.uri,
            position
        );
        
        assert.ok(completions, 'Completions should be returned');
        
        const hasStdlib = completions.items.some(
            item => typeof item.label === 'string' 
                ? item.label === 'stdlib'
                : item.label.label === 'stdlib'
        );
        
        console.log(`Found ${completions.items.length} completions`);
        if (!hasStdlib) {
            const labels = completions.items.map(item => 
                typeof item.label === 'string' ? item.label : item.label.label
            ).slice(0, 10);
            console.log('First 10 completions:', labels);
        }
        
        assert.ok(hasStdlib, 'Should suggest "stdlib" in completions');
    });

    test('Should suggest "fmt", "fs", "sys" after "stdlib."', async function() {
        const editor = vscode.window.activeTextEditor;
        assert.ok(editor, 'Editor should be active');
        
        // Replace content with "stdlib."
        await editor.edit(editBuilder => {
            const entireRange = new vscode.Range(
                document.positionAt(0),
                document.positionAt(document.getText().length)
            );
            editBuilder.replace(entireRange, 'function main() int\n    stdlib.\nend \'main\'');
        });
        
        const position = new vscode.Position(1, 11); // After "stdlib."
        
        const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
            'vscode.executeCompletionItemProvider',
            document.uri,
            position,
            '.' // Trigger character
        );
        
        assert.ok(completions, 'Completions should be returned');
        
        const labels = completions.items.map(item => 
            typeof item.label === 'string' ? item.label : item.label.label
        );
        
        console.log(`Found ${completions.items.length} completions after "stdlib."`);
        console.log('Completions:', labels);
        
        const hasFmt = labels.includes('fmt');
        
        assert.ok(hasFmt, 'Should suggest "fmt" after "stdlib."');
    });

    test('Should suggest modules after "stdlib.fmt."', async function() {
        const editor = vscode.window.activeTextEditor;
        assert.ok(editor, 'Editor should be active');
        
        await editor.edit(editBuilder => {
            const entireRange = new vscode.Range(
                document.positionAt(0),
                document.positionAt(document.getText().length)
            );
            editBuilder.replace(entireRange, 'function main() int\n    stdlib.fmt.\nend \'main\'');
        });
        
        const position = new vscode.Position(1, 15); // After "stdlib.fmt."
        
        const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
            'vscode.executeCompletionItemProvider',
            document.uri,
            position,
            '.' // Trigger character
        );
        
        assert.ok(completions, 'Completions should be returned');
        
        const labels = completions.items.map(item => 
            typeof item.label === 'string' ? item.label : item.label.label
        );
        
        console.log(`Found ${completions.items.length} completions after "stdlib.fmt."`);
        console.log('Completions:', labels);
        
        const hasInteger = labels.includes('integer');
        
        assert.ok(hasInteger, 'Should suggest "integer" module after "stdlib.fmt."');
    });

    test('Should suggest functions after "stdlib.fmt.integer."', async function() {
        const editor = vscode.window.activeTextEditor;
        assert.ok(editor, 'Editor should be active');
        
        await editor.edit(editBuilder => {
            const entireRange = new vscode.Range(
                document.positionAt(0),
                document.positionAt(document.getText().length)
            );
            editBuilder.replace(entireRange, 'function main() int\n    stdlib.fmt.integer.\nend \'main\'');
        });
        
        const position = new vscode.Position(1, 23); // After "stdlib.fmt.integer."
        
        const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
            'vscode.executeCompletionItemProvider',
            document.uri,
            position,
            '.' // Trigger character
        );
        
        assert.ok(completions, 'Completions should be returned');
        
        const labels = completions.items.map(item => 
            typeof item.label === 'string' ? item.label : item.label.label
        );
        
        console.log(`Found ${completions.items.length} completions after "stdlib.fmt.integer."`);
        console.log('Completions:', labels);
        
        const hasFormatIntArray = labels.includes('format_int_array');
        
        assert.ok(hasFormatIntArray, 'Should suggest "format_int_array" after "stdlib.fmt.integer."');
    });

    test('Should provide function details in qualified name completion', async function() {
        const editor = vscode.window.activeTextEditor;
        assert.ok(editor, 'Editor should be active');
        
        await editor.edit(editBuilder => {
            const entireRange = new vscode.Range(
                document.positionAt(0),
                document.positionAt(document.getText().length)
            );
            editBuilder.replace(entireRange, 'function main() int\n    stdlib.fmt.integer.\nend \'main\'');
        });
        
        const position = new vscode.Position(1, 23); // After "stdlib.fmt.integer."
        
        const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
            'vscode.executeCompletionItemProvider',
            document.uri,
            position,
            '.' // Trigger character
        );
        
        assert.ok(completions, 'Completions should be returned');
        
        const formatIntArrayItem = completions.items.find(item => {
            const label = typeof item.label === 'string' ? item.label : item.label.label;
            return label === 'format_int_array';
        });
        
        if (formatIntArrayItem) {
            console.log('format_int_array completion item:', {
                label: formatIntArrayItem.label,
                kind: formatIntArrayItem.kind,
                detail: formatIntArrayItem.detail,
                documentation: formatIntArrayItem.documentation
            });
            
            // Check that it has function signature details
            assert.ok(formatIntArrayItem.detail, 'Should have detail property');
            if (typeof formatIntArrayItem.detail === 'string') {
                assert.ok(
                    formatIntArrayItem.detail.includes('value int') || 
                    formatIntArrayItem.detail.includes('buffer'),
                    'Should include function signature in detail'
                );
            }
            
            // Check that it's marked as a function
            assert.strictEqual(formatIntArrayItem.kind, vscode.CompletionItemKind.Function);
        } else {
            assert.fail('format_int_array not found in completions');
        }
    });

    test('Should handle multiline context for qualified names', async function() {
        const editor = vscode.window.activeTextEditor;
        assert.ok(editor, 'Editor should be active');
        
        await editor.edit(editBuilder => {
            const entireRange = new vscode.Range(
                document.positionAt(0),
                document.positionAt(document.getText().length)
            );
            editBuilder.replace(entireRange, 
                'function main() int\n' +
                '    var x int = 42\n' +
                '    var buffer [12]char = 0\n' +
                '    stdlib.fmt.\n' +
                'end \'main\''
            );
        });
        
        const position = new vscode.Position(3, 15); // After "stdlib.fmt." on line 4
        
        const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
            'vscode.executeCompletionItemProvider',
            document.uri,
            position,
            '.' // Trigger character
        );
        
        assert.ok(completions, 'Completions should be returned');
        
        const labels = completions.items.map(item => 
            typeof item.label === 'string' ? item.label : item.label.label
        );
        
        const hasInteger = labels.includes('integer');
        
        assert.ok(hasInteger, 'Should suggest "integer" in multiline context');
    });
});
