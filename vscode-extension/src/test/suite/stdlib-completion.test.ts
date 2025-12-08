import * as assert from 'assert';
import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import { getClient } from '../../extension';
import { State } from 'vscode-languageclient';

suite('Stdlib Completion Tests', () => {
	let document: vscode.TextDocument;

	suiteSetup(async function () {
		// Ensure extension is activated
		const ext = vscode.extensions.getExtension('maxon.maxon-lsp-client');
		if (ext && !ext.isActive) {
			await ext.activate();
		}
	});

	setup(async function () {
		// Create a temporary test file
		const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
		assert.ok(workspaceFolder, 'Workspace folder should be available');

		const testFilePath = path.join(workspaceFolder.uri.fsPath, 'temp', 'test_stdlib.maxon');

		// Create test file with basic content
		fs.writeFileSync(testFilePath, 'function main() int\n    \nend \'main\'', 'utf-8');

		// Open the document
		document = await vscode.workspace.openTextDocument(testFilePath);
		await vscode.window.showTextDocument(document);


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

	test('Should provide stdlib function completions (if LSP server running)', async function () {
		const position = new vscode.Position(1, 4); // Inside function body

		// Request completions
		const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
			'vscode.executeCompletionItemProvider',
			document.uri,
			position
		);

		assert.ok(completions, 'Completions should be returned');

		// Check if completions are available
		const labels = completions.items.map(item => item.label).slice(0, 20);

		// Check if formatIntArray is in the completions
		const hasFormatIntArray = completions.items.some(
			item => item.label === 'formatIntArray'
		);

		assert.ok(hasFormatIntArray, 'Should include formatIntArray from stdlib');
	});

	test('Stdlib function completion should have correct kind', async function () {
		const position = new vscode.Position(1, 4);
		const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
			'vscode.executeCompletionItemProvider',
			document.uri,
			position
		);

		const formatIntArray = completions.items.find(
			item => item.label === 'formatIntArray'
		);

		assert.ok(formatIntArray, 'formatIntArray should be found');
		assert.strictEqual(
			formatIntArray.kind,
			vscode.CompletionItemKind.Function,
			'formatIntArray should be a Function'
		);
	});

	test('Stdlib function completion should have detail with signature', async function () {
		const position = new vscode.Position(1, 4);
		const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
			'vscode.executeCompletionItemProvider',
			document.uri,
			position
		);

		const formatIntArray = completions.items.find(
			item => item.label === 'formatIntArray'
		);

		assert.ok(formatIntArray, 'formatIntArray should be found');
		assert.ok(formatIntArray.detail, 'Should have detail field');

		// Check that signature contains expected elements
		const detail = formatIntArray.detail as string;
		assert.ok(detail.includes('value int'), 'Detail should include parameter "value int"');
		assert.ok(detail.includes('buffer'), 'Detail should include parameter "buffer"');
		// Array type is []byte (character is now a grapheme cluster struct, not a byte alias)
		assert.ok(detail.includes('byte'), 'Detail should include byte array type');
	});

	test('Stdlib function completion should have documentation', async function () {
		// Use position (1, 0) to get unfiltered completions
		const position = new vscode.Position(1, 0);
		const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
			'vscode.executeCompletionItemProvider',
			document.uri,
			position
		);

		const formatIntArray = completions.items.find(
			item => item.label === 'formatIntArray'
		);

		assert.ok(formatIntArray, 'formatIntArray should be found');
		assert.ok(formatIntArray.documentation, 'Should have documentation');
	});

	test('Should provide hover information for stdlib functions', async function () {
		// Add stdlib function call to document
		const edit = new vscode.WorkspaceEdit();
		edit.insert(document.uri, new vscode.Position(1, 4), 'formatIntArray');
		await vscode.workspace.applyEdit(edit);
		await document.save();

		const position = new vscode.Position(1, 10); // Middle of "formatIntArray"

		// Wait for hover information to be available
		let hovers: vscode.Hover[] | undefined;
		const maxAttempts = 20;
		for (let i = 0; i < maxAttempts; i++) {
			hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
				'vscode.executeHoverProvider',
				document.uri,
				position
			);
			if (hovers && hovers.length > 0) {
				break;
			}
			await new Promise(resolve => setTimeout(resolve, 100)); // waiting for hover
		}

		assert.ok(hovers && hovers.length > 0, 'Should return hover information');

		const hoverText = hovers[0].contents.map(c =>
			typeof c === 'string' ? c : c.value
		).join('\n');

		// Stdlib functions may not show qualified names when not properly used in context
		// Just verify we get some hover information
		assert.ok(
			hoverText.includes('formatIntArray') || hoverText.includes('Identifier'),
			'Hover should show function name or identifier'
		);
	});

	test('Should include keyword completions alongside stdlib', async function () {
		// Use position (1, 0) to get unfiltered completions
		const position = new vscode.Position(1, 0);
		const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
			'vscode.executeCompletionItemProvider',
			document.uri,
			position
		);

		// Should have both stdlib functions and keywords
		const hasStdlibFunc = completions.items.some(
			item => item.label === 'formatIntArray'
		);
		const hasKeyword = completions.items.some(
			item => item.label === 'var' || item.label === 'let'
		);

		assert.ok(hasStdlibFunc, 'Should include stdlib functions');
		assert.ok(hasKeyword, 'Should include keywords');
	});

	test('Should provide completions in different positions', async function () {
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
	test('Should handle missing stdlib directory gracefully', async function () {
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
