import * as assert from 'assert';
import * as vscode from 'vscode';
import * as path from 'path';

/**
 * Test suite for Code Action (Quick Fix) functionality
 */
suite('Code Action Test Suite', () => {
    
    let testDocument: vscode.TextDocument | undefined;
    let testEditor: vscode.TextEditor | undefined;

    setup(async () => {
        // Wait for extension to activate
        const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
        if (ext && !ext.isActive) {
            await ext.activate();
        }
        
        // Give the language server time to start
        await new Promise(resolve => setTimeout(resolve, 2000));
    });

    teardown(async () => {
        // Close test documents
        if (testDocument) {
            await vscode.window.showTextDocument(testDocument);
            await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
            testDocument = undefined;
        }
        testEditor = undefined;
    });

    test('Should provide code actions for unused variable warning', async function() {
        this.timeout(10000); // Increase timeout for LSP communication
        
        // Create a test document with an unused variable
        const testContent = [
            "function test() int",
            "    var unused = 42",
            "    var used = 10",
            "    return used",
            "end 'test'"
        ].join('\n');
        
        const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
        assert.ok(workspaceFolder, 'Workspace folder should exist');
        
        const testFilePath = path.join(workspaceFolder.uri.fsPath, 'test_unused.maxon');
        const testUri = vscode.Uri.file(testFilePath);
        
        // Create and open the document
        const edit = new vscode.WorkspaceEdit();
        edit.createFile(testUri, { ignoreIfExists: true, overwrite: true });
        edit.insert(testUri, new vscode.Position(0, 0), testContent);
        await vscode.workspace.applyEdit(edit);
        
        testDocument = await vscode.workspace.openTextDocument(testUri);
        testEditor = await vscode.window.showTextDocument(testDocument);
        
        // Wait for diagnostics to be published
        await new Promise(resolve => setTimeout(resolve, 2000));
        
        // Get diagnostics for the document
        const diagnostics = vscode.languages.getDiagnostics(testUri);
        assert.ok(diagnostics.length > 0, 'Should have at least one diagnostic');
        
        // Find the unused variable warning
        const unusedVarDiagnostic = diagnostics.find(d => 
            d.message.includes('unused') && d.severity === vscode.DiagnosticSeverity.Warning
        );
        assert.ok(unusedVarDiagnostic, 'Should have unused variable warning');
        
        // Request code actions for the diagnostic range
        const codeActions = await vscode.commands.executeCommand<vscode.CodeAction[]>(
            'vscode.executeCodeActionProvider',
            testUri,
            unusedVarDiagnostic.range
        );
        
        assert.ok(codeActions, 'Should return code actions');
        assert.ok(codeActions.length > 0, 'Should have at least one code action');
        
        // Check for the "Remove unused variable" action
        const removeAction = codeActions.find(action => 
            action.title.includes('Remove unused variable')
        );
        assert.ok(removeAction, 'Should have "Remove unused variable" action');
        assert.strictEqual(removeAction.kind?.value, 'quickfix', 'Should be a quickfix action');
        
        // Clean up
        await vscode.workspace.fs.delete(testUri);
    });

    test('Code action should have correct structure', async function() {
        this.timeout(10000);
        
        const testContent = [
            "function test() int",
            "    var myUnused = 123",
            "    return 0",
            "end 'test'"
        ].join('\n');
        
        const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
        assert.ok(workspaceFolder, 'Workspace folder should exist');
        
        const testFilePath = path.join(workspaceFolder.uri.fsPath, 'test_structure.maxon');
        const testUri = vscode.Uri.file(testFilePath);
        
        const edit = new vscode.WorkspaceEdit();
        edit.createFile(testUri, { ignoreIfExists: true, overwrite: true });
        edit.insert(testUri, new vscode.Position(0, 0), testContent);
        await vscode.workspace.applyEdit(edit);
        
        testDocument = await vscode.workspace.openTextDocument(testUri);
        testEditor = await vscode.window.showTextDocument(testDocument);
        
        await new Promise(resolve => setTimeout(resolve, 2000));
        
        const diagnostics = vscode.languages.getDiagnostics(testUri);
        const unusedVarDiagnostic = diagnostics.find(d => 
            d.message.includes('myUnused') && d.severity === vscode.DiagnosticSeverity.Warning
        );
        
        if (unusedVarDiagnostic) {
            const codeActions = await vscode.commands.executeCommand<vscode.CodeAction[]>(
                'vscode.executeCodeActionProvider',
                testUri,
                unusedVarDiagnostic.range
            );
            
            assert.ok(codeActions && codeActions.length > 0, 'Should have code actions');
            
            const removeAction = codeActions.find(action => 
                action.title.includes('Remove unused variable')
            );
            
            if (removeAction) {
                // Verify the action has an edit
                assert.ok(removeAction.edit, 'Code action should have an edit');
                
                // Verify the edit has changes
                if (removeAction.edit) {
                    const changes = removeAction.edit.entries();
                    assert.ok(changes.length > 0, 'Edit should have changes');
                }
            }
        }
        
        await vscode.workspace.fs.delete(testUri);
    });

    test('Should not provide code actions for errors', async function() {
        this.timeout(10000);
        
        // Create a document with a parse error
        const testContent = [
            "function test() int",
            "    this is invalid syntax",
            "end 'test'"
        ].join('\n');
        
        const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
        assert.ok(workspaceFolder, 'Workspace folder should exist');
        
        const testFilePath = path.join(workspaceFolder.uri.fsPath, 'test_error.maxon');
        const testUri = vscode.Uri.file(testFilePath);
        
        const edit = new vscode.WorkspaceEdit();
        edit.createFile(testUri, { ignoreIfExists: true, overwrite: true });
        edit.insert(testUri, new vscode.Position(0, 0), testContent);
        await vscode.workspace.applyEdit(edit);
        
        testDocument = await vscode.workspace.openTextDocument(testUri);
        testEditor = await vscode.window.showTextDocument(testDocument);
        
        await new Promise(resolve => setTimeout(resolve, 2000));
        
        const diagnostics = vscode.languages.getDiagnostics(testUri);
        const errorDiagnostic = diagnostics.find(d => 
            d.severity === vscode.DiagnosticSeverity.Error
        );
        
        if (errorDiagnostic) {
            const codeActions = await vscode.commands.executeCommand<vscode.CodeAction[]>(
                'vscode.executeCodeActionProvider',
                testUri,
                errorDiagnostic.range
            );
            
            // Should not have "Remove unused variable" action for errors
            const removeAction = codeActions?.find(action => 
                action.title.includes('Remove unused variable')
            );
            assert.ok(!removeAction, 'Should not have quick fix for errors');
        }
        
        await vscode.workspace.fs.delete(testUri);
    });

    test('Code action should remove entire line', async function() {
        this.timeout(10000);
        
        const testContent = [
            "function test() int",
            "    var toRemove = 99",
            "    var keepThis = 10",
            "    return keepThis",
            "end 'test'"
        ].join('\n');
        
        const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
        assert.ok(workspaceFolder, 'Workspace folder should exist');
        
        const testFilePath = path.join(workspaceFolder.uri.fsPath, 'test_remove.maxon');
        const testUri = vscode.Uri.file(testFilePath);
        
        const edit = new vscode.WorkspaceEdit();
        edit.createFile(testUri, { ignoreIfExists: true, overwrite: true });
        edit.insert(testUri, new vscode.Position(0, 0), testContent);
        await vscode.workspace.applyEdit(edit);
        
        testDocument = await vscode.workspace.openTextDocument(testUri);
        testEditor = await vscode.window.showTextDocument(testDocument);
        
        await new Promise(resolve => setTimeout(resolve, 2000));
        
        const diagnostics = vscode.languages.getDiagnostics(testUri);
        const unusedVarDiagnostic = diagnostics.find(d => 
            d.message.includes('toRemove') && d.severity === vscode.DiagnosticSeverity.Warning
        );
        
        if (unusedVarDiagnostic) {
            const codeActions = await vscode.commands.executeCommand<vscode.CodeAction[]>(
                'vscode.executeCodeActionProvider',
                testUri,
                unusedVarDiagnostic.range
            );
            
            const removeAction = codeActions?.find(action => 
                action.title.includes('Remove unused variable')
            );
            
            if (removeAction && removeAction.edit) {
                // Apply the edit
                await vscode.workspace.applyEdit(removeAction.edit);
                
                // Verify the line was removed
                const updatedContent = testDocument.getText();
                assert.ok(!updatedContent.includes('toRemove'), 'Unused variable should be removed');
                assert.ok(updatedContent.includes('keepThis'), 'Other variables should remain');
            }
        }
        
        await vscode.workspace.fs.delete(testUri);
    });

    test('Diagnostic should include code field', async function() {
        this.timeout(10000);
        
        const testContent = [
            "function test() int",
            "    var unused = 1",
            "    return 0",
            "end 'test'"
        ].join('\n');
        
        const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
        assert.ok(workspaceFolder, 'Workspace folder should exist');
        
        const testFilePath = path.join(workspaceFolder.uri.fsPath, 'test_code.maxon');
        const testUri = vscode.Uri.file(testFilePath);
        
        const edit = new vscode.WorkspaceEdit();
        edit.createFile(testUri, { ignoreIfExists: true, overwrite: true });
        edit.insert(testUri, new vscode.Position(0, 0), testContent);
        await vscode.workspace.applyEdit(edit);
        
        testDocument = await vscode.workspace.openTextDocument(testUri);
        testEditor = await vscode.window.showTextDocument(testDocument);
        
        await new Promise(resolve => setTimeout(resolve, 2000));
        
        const diagnostics = vscode.languages.getDiagnostics(testUri);
        const unusedVarDiagnostic = diagnostics.find(d => 
            d.message.includes('unused') && d.severity === vscode.DiagnosticSeverity.Warning
        );
        
        if (unusedVarDiagnostic) {
            // Check if diagnostic has a code
            assert.ok(unusedVarDiagnostic.code, 'Diagnostic should have a code');
            
            // The code should be "unused-variable"
            if (typeof unusedVarDiagnostic.code === 'string') {
                assert.strictEqual(unusedVarDiagnostic.code, 'unused-variable', 
                    'Diagnostic code should be "unused-variable"');
            } else if (typeof unusedVarDiagnostic.code === 'object' && 'value' in unusedVarDiagnostic.code) {
                assert.strictEqual(unusedVarDiagnostic.code.value, 'unused-variable',
                    'Diagnostic code should be "unused-variable"');
            }
        }
        
        await vscode.workspace.fs.delete(testUri);
    });

    test('Multiple unused variables should each have quick fixes', async function() {
        this.timeout(10000);
        
        const testContent = [
            "function test() int",
            "    var unused1 = 1",
            "    var unused2 = 2",
            "    var used = 3",
            "    return used",
            "end 'test'"
        ].join('\n');
        
        const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
        assert.ok(workspaceFolder, 'Workspace folder should exist');
        
        const testFilePath = path.join(workspaceFolder.uri.fsPath, 'test_multiple.maxon');
        const testUri = vscode.Uri.file(testFilePath);
        
        const edit = new vscode.WorkspaceEdit();
        edit.createFile(testUri, { ignoreIfExists: true, overwrite: true });
        edit.insert(testUri, new vscode.Position(0, 0), testContent);
        await vscode.workspace.applyEdit(edit);
        
        testDocument = await vscode.workspace.openTextDocument(testUri);
        testEditor = await vscode.window.showTextDocument(testDocument);
        
        await new Promise(resolve => setTimeout(resolve, 2000));
        
        const diagnostics = vscode.languages.getDiagnostics(testUri);
        const unusedVarDiagnostics = diagnostics.filter(d => 
            d.message.includes('unused') && d.severity === vscode.DiagnosticSeverity.Warning
        );
        
        // Should have 2 unused variable warnings
        assert.ok(unusedVarDiagnostics.length >= 2, 'Should have multiple unused variable warnings');
        
        // Each should have a code action
        for (const diagnostic of unusedVarDiagnostics) {
            const codeActions = await vscode.commands.executeCommand<vscode.CodeAction[]>(
                'vscode.executeCodeActionProvider',
                testUri,
                diagnostic.range
            );
            
            const removeAction = codeActions?.find(action => 
                action.title.includes('Remove unused variable')
            );
            assert.ok(removeAction, 'Each unused variable should have a quick fix');
        }
        
        await vscode.workspace.fs.delete(testUri);
    });
});
